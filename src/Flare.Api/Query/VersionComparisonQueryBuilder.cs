using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized query plus its bound parameters (one collection per query - same reasoning as <see cref="ServiceDependencyGraphSql"/>).</summary>
public sealed record VersionComparisonSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>The four "what changed" queries for one resolved (baseline, current) pair.</summary>
public sealed record VersionComparisonQueries(
    VersionComparisonSql Endpoints,
    VersionComparisonSql NewExceptions,
    VersionComparisonSql NewDependencies,
    VersionComparisonSql NewLogPatterns);

/// <summary>
/// Pure SQL builder for the deploy / version comparison (<c>POST /api/services/version-comparison</c>).
/// A version is the <c>service.version</c> resource attribute on spans and logs, so this needs no
/// new storage. <see cref="BuildVersions"/> lists the versions a service sent within the lookback,
/// ordered by first-seen; <see cref="Build"/> then compares one pair.
/// </summary>
/// <remarks>
/// "New" is decided in SQL (<c>HAVING countIf(current) &gt; 0 AND countIf(baseline) = 0</c>) so only
/// the differences come back, not every exception type and log pattern the service has. Because
/// every query is bounded by the lookback, a baseline version that stopped sending before it
/// began shows everything as new; the dashboard says which window it compared.
/// </remarks>
public static class VersionComparisonQueryBuilder
{
    public const int DefaultLookbackHours = 168;
    public const int MaxLookbackHours = 720;

    /// <summary>Cap on versions listed, newest first-seen first.</summary>
    public const int MaxVersions = 50;

    /// <summary>Cap on rows of each comparison result.</summary>
    public const int MaxRows = 100;

    private const string VersionExpr = "ResourceAttributes['service.version']";

    /// <summary>OTel SpanKind server (2) and consumer (5): the spans a service serves, i.e. its endpoints.</summary>
    private const string EntryKinds = "Kind IN (2, 5)";

    public static int ClampLookbackHours(int? requested) =>
        Math.Clamp(requested is > 0 ? requested.Value : DefaultLookbackHours, 1, MaxLookbackHours);

    /// <summary>
    /// Picks the pair to compare. Versions arrive newest first-seen first. An explicit choice
    /// wins; otherwise current is the newest and baseline the one first seen just before current.
    /// Either is null when it can't be resolved (no such version, or only one version exists).
    /// </summary>
    public static (string? Baseline, string? Current) ResolvePair(
        IReadOnlyList<ServiceVersionInfo> versions, string? requestedBaseline, string? requestedCurrent)
    {
        bool Known(string? v) => !string.IsNullOrEmpty(v) && versions.Any(x => x.Version == v);

        var current = Known(requestedCurrent) ? requestedCurrent : versions.FirstOrDefault()?.Version;
        string? baseline;
        if (Known(requestedBaseline) && requestedBaseline != current)
        {
            baseline = requestedBaseline;
        }
        else
        {
            var currentIndex = current is null ? -1 : versions.ToList().FindIndex(v => v.Version == current);
            baseline = currentIndex >= 0 && currentIndex + 1 < versions.Count ? versions[currentIndex + 1].Version : null;
        }

        return (baseline, baseline is null ? null : current);
    }

    public static VersionComparisonSql BuildVersions(string service, DateTimeOffset from, DateTimeOffset to)
    {
        var parameters = TimeParameters(service, from, to);
        parameters.AddParameter("limit", (uint)MaxVersions);

        var sql = "SELECT\n" +
            $"    {VersionExpr} AS Version,\n" +
            "    min(StartTime) AS FirstSeen,\n" +
            "    max(StartTime) AS LastSeen,\n" +
            "    count() AS SpanCount\n" +
            "FROM spans\n" +
            "WHERE ServiceName = {service:String}\n" +
            "    AND StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}\n" +
            $"    AND {VersionExpr} != ''\n" +
            "GROUP BY Version\n" +
            "ORDER BY FirstSeen DESC\n" +
            "LIMIT {limit:UInt32}";
        return new VersionComparisonSql(sql, parameters);
    }

    public static VersionComparisonQueries Build(string service, string baseline, string current, DateTimeOffset from, DateTimeOffset to)
    {
        var spanWhere = "ServiceName = {service:String}\n" +
            "    AND StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}\n" +
            $"    AND {VersionExpr} IN ({{baseline:String}}, {{current:String}})";
        var isCurrent = $"{VersionExpr} = {{current:String}}";
        var isBaseline = $"{VersionExpr} = {{baseline:String}}";
        var onlyInCurrent = $"HAVING countIf({isCurrent}) > 0 AND countIf({isBaseline}) = 0\n";

        var endpoints = new VersionComparisonSql(
            "SELECT\n" +
            "    Name AS Endpoint,\n" +
            $"    {VersionExpr} AS Version,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(StatusCode = {errorStatus:String}) AS ErrorCount,\n" +
            "    quantile(0.95)(DurationNano) AS P95DurationNano\n" +
            "FROM spans\n" +
            $"WHERE {spanWhere}\n    AND {EntryKinds}\n" +
            "GROUP BY Endpoint, Version\n" +
            "ORDER BY CallCount DESC\n" +
            "LIMIT {limit:UInt32}",
            Pair(service, baseline, current, from, to));

        var exceptions = new VersionComparisonSql(
            "SELECT\n" +
            "    EventAttributes['exception.type'] AS ExceptionType,\n" +
            "    countIf(" + isCurrent + ") AS Occurrences,\n" +
            "    min(EventTime) AS FirstSeen\n" +
            "FROM spans\n" +
            "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            $"WHERE {spanWhere}\n    AND EventName = 'exception' AND EventAttributes['exception.type'] != ''\n" +
            "GROUP BY ExceptionType\n" +
            onlyInCurrent +
            "ORDER BY Occurrences DESC\n" +
            "LIMIT {limit:UInt32}",
            Pair(service, baseline, current, from, to));

        string DependencyPart(string kind, string targetExpr) =>
            "SELECT\n" +
            $"    '{kind}' AS DependencyKind,\n" +
            $"    {targetExpr} AS Target,\n" +
            "    countIf(" + isCurrent + ") AS CallCount,\n" +
            "    countIf(" + isCurrent + " AND StatusCode = {errorStatus:String}) AS ErrorCount\n" +
            "FROM spans\n" +
            $"WHERE {spanWhere}\n    AND {targetExpr} != ''\n" +
            "GROUP BY Target\n" +
            onlyInCurrent;
        var dependencies = new VersionComparisonSql(
            "SELECT DependencyKind, Target, CallCount, ErrorCount FROM (\n" +
            DependencyPart("External", ServiceCallBreakdownQueryBuilder.ExternalTargetExpr) +
            "UNION ALL\n" +
            DependencyPart("Database", ServiceCallBreakdownQueryBuilder.DbSystemExpr) +
            ")\nORDER BY CallCount DESC\nLIMIT {limit:UInt32}",
            Pair(service, baseline, current, from, to));

        var patterns = new VersionComparisonSql(
            "SELECT\n" +
            "    PatternId,\n" +
            "    any(PatternTemplate) AS Template,\n" +
            "    countIf(" + isCurrent + ") AS Occurrences,\n" +
            "    max(SeverityNumber) AS Severity,\n" +
            "    min(Timestamp) AS FirstSeen\n" +
            "FROM logs\n" +
            "WHERE ServiceName = {service:String}\n" +
            "    AND Timestamp >= {from:DateTime64(9)} AND Timestamp < {to:DateTime64(9)}\n" +
            $"    AND {VersionExpr} IN ({{baseline:String}}, {{current:String}})\n" +
            "    AND PatternId != ''\n" +
            "GROUP BY PatternId\n" +
            onlyInCurrent +
            "ORDER BY Severity DESC, Occurrences DESC\n" +
            "LIMIT {limit:UInt32}",
            Pair(service, baseline, current, from, to));

        return new VersionComparisonQueries(endpoints, exceptions, dependencies, patterns);
    }

    private static ClickHouseParameterCollection TimeParameters(string service, DateTimeOffset from, DateTimeOffset to)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("service", service);
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        return parameters;
    }

    private static ClickHouseParameterCollection Pair(string service, string baseline, string current, DateTimeOffset from, DateTimeOffset to)
    {
        var parameters = TimeParameters(service, from, to);
        parameters.AddParameter("baseline", baseline);
        parameters.AddParameter("current", current);
        parameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");
        parameters.AddParameter("limit", (uint)MaxRows);
        return parameters;
    }
}
