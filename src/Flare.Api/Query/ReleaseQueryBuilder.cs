using System.Security.Cryptography;
using System.Text;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

public sealed record ReleaseSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builders for release tracking. An exception group (<c>exception.type</c> +
/// <c>exception.message</c>, the same grouping as the Errors page) is <em>introduced</em> by the
/// <c>service.version</c> of its earliest exception event within the history window, so no per-event
/// release table is needed. See <c>docs-internal/adr/0182-release-tracking.md</c>.
/// </summary>
public static class ReleaseQueryBuilder
{
    /// <summary>How far before a deploy earlier occurrences are looked for; a group last seen longer ago reads as new.</summary>
    public const int HistoryDays = 30;

    public const int MaxErrors = 200;

    private const string VersionExpr = "ResourceAttributes['service.version']";

    /// <summary>The release's stable key: 32 hex chars of SHA-256 over service, a unit separator and version.</summary>
    public static string ComputeId(string service, string version) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(service + '\u001f' + version)).AsSpan(0, 16));

    /// <summary>
    /// Columns: first version, number of groups first seen under it. One row per version that
    /// introduced anything; the caller keeps the versions it has marked as releases.
    /// </summary>
    public static ReleaseSql BuildNewErrorCounts(string service, DateTimeOffset earliestDeploy)
    {
        var parameters = Parameters(service, earliestDeploy);
        var sql = "SELECT FirstVersion, count() AS Groups\n" +
            $"FROM (\n{FirstSeenSelect(parameters)}\n)\n" +
            "WHERE FirstVersion != ''\n" +
            "GROUP BY FirstVersion";
        return new ReleaseSql(sql, parameters);
    }

    /// <summary>Columns: type, message, occurrences, first seen, last seen - the groups one version introduced, most frequent first.</summary>
    public static ReleaseSql BuildNewErrors(string service, string version, DateTimeOffset deployedAt)
    {
        var parameters = Parameters(service, deployedAt);
        parameters.AddParameter("version", version);
        parameters.AddParameter("limit", (uint)MaxErrors);
        var sql = "SELECT ExceptionType, ExceptionMessage, Occurrences, FirstSeen, LastSeen\n" +
            $"FROM (\n{FirstSeenSelect(parameters)}\n)\n" +
            "WHERE FirstVersion = {version:String}\n" +
            "ORDER BY Occurrences DESC\n" +
            "LIMIT {limit:UInt32}";
        return new ReleaseSql(sql, parameters);
    }

    private static string FirstSeenSelect(ClickHouseParameterCollection parameters) =>
        "SELECT\n" +
        "    EventAttributes['exception.type'] AS ExceptionType,\n" +
        "    EventAttributes['exception.message'] AS ExceptionMessage,\n" +
        $"    argMin({VersionExpr}, EventTime) AS FirstVersion,\n" +
        "    count() AS Occurrences,\n" +
        "    min(EventTime) AS FirstSeen,\n" +
        "    max(EventTime) AS LastSeen\n" +
        "FROM spans\n" +
        "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
        "WHERE ServiceName = {service:String} AND StartTime >= {from:DateTime64(9)}\n" +
        "    AND EventName = 'exception' AND EventAttributes['exception.type'] != ''" + ServiceScope.Suffix(parameters) + "\n" +
        "GROUP BY ExceptionType, ExceptionMessage";

    private static ClickHouseParameterCollection Parameters(string service, DateTimeOffset deployedAt)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("service", service);
        parameters.AddParameter("from", deployedAt.UtcDateTime.AddDays(-HistoryDays));
        return parameters;
    }
}
