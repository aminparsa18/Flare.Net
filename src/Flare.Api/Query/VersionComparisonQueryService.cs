using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IVersionComparisonQueryService
{
    Task<VersionComparisonResponse> CompareAsync(string service, VersionComparisonRequest request, CancellationToken cancellationToken);
}

/// <summary>The one component holding an <see cref="IClickHouseClient"/> for version comparison - SQL in <see cref="VersionComparisonQueryBuilder"/>.</summary>
public sealed class VersionComparisonQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IVersionComparisonQueryService
{
    public async Task<VersionComparisonResponse> CompareAsync(string service, VersionComparisonRequest request, CancellationToken cancellationToken)
    {
        var lookbackHours = VersionComparisonQueryBuilder.ClampLookbackHours(request.LookbackHours);
        var to = timeProvider.GetUtcNow();
        var from = to - TimeSpan.FromHours(lookbackHours);

        var versions = await ReadAsync(VersionComparisonQueryBuilder.BuildVersions(service, from, to), reader => new ServiceVersionInfo
        {
            Version = reader.GetString(0),
            FirstSeenUnixMs = ToUnixMs(reader.GetDateTime(1)),
            LastSeenUnixMs = ToUnixMs(reader.GetDateTime(2)),
            SpanCount = (long)reader.GetFieldValue<ulong>(3),
        }, cancellationToken);

        var (baseline, current) = VersionComparisonQueryBuilder.ResolvePair(versions, request.BaselineVersion, request.CurrentVersion);
        if (baseline is null || current is null)
        {
            return new VersionComparisonResponse
            {
                LookbackHours = lookbackHours,
                Versions = versions,
                CurrentVersion = current ?? versions.FirstOrDefault()?.Version,
                Endpoints = [],
                NewExceptions = [],
                NewDependencies = [],
                NewLogPatterns = [],
            };
        }

        var queries = VersionComparisonQueryBuilder.Build(service, baseline, current, from, to);
        var endpointRows = await ReadAsync(queries.Endpoints, reader => (
            Endpoint: reader.GetString(0),
            Version: reader.GetString(1),
            Stats: new VersionEndpointStats
            {
                Count = (long)reader.GetFieldValue<ulong>(2),
                ErrorCount = (long)reader.GetFieldValue<ulong>(3),
                P95DurationMs = reader.GetDouble(4) / 1_000_000d,
            }), cancellationToken);
        var exceptions = await ReadAsync(queries.NewExceptions, reader => new VersionNewException
        {
            ExceptionType = reader.GetString(0),
            Count = (long)reader.GetFieldValue<ulong>(1),
            FirstSeenUnixMs = ToUnixMs(reader.GetDateTime(2)),
        }, cancellationToken);
        var dependencies = await ReadAsync(queries.NewDependencies, reader => new VersionNewDependency
        {
            Kind = reader.GetString(0),
            Target = reader.GetString(1),
            CallCount = (long)reader.GetFieldValue<ulong>(2),
            ErrorCount = (long)reader.GetFieldValue<ulong>(3),
        }, cancellationToken);
        var patterns = await ReadAsync(queries.NewLogPatterns, reader => new VersionNewLogPattern
        {
            PatternId = reader.GetString(0),
            Template = reader.GetString(1),
            Count = (long)reader.GetFieldValue<ulong>(2),
            MaxSeverityNumber = reader.GetByte(3),
            FirstSeenUnixMs = ToUnixMs(reader.GetDateTime(4)),
        }, cancellationToken);

        return new VersionComparisonResponse
        {
            LookbackHours = lookbackHours,
            Versions = versions,
            BaselineVersion = baseline,
            CurrentVersion = current,
            Endpoints = PairEndpoints(endpointRows, baseline, current),
            NewExceptions = exceptions,
            NewDependencies = dependencies,
            NewLogPatterns = patterns,
        };
    }

    /// <summary>Folds the per-(endpoint, version) rows into one row per endpoint, keeping the highest-volume first order.</summary>
    internal static List<VersionEndpointComparison> PairEndpoints(
        IEnumerable<(string Endpoint, string Version, VersionEndpointStats Stats)> rows, string baseline, string current) =>
        rows.GroupBy(r => r.Endpoint)
            .Select(g => new VersionEndpointComparison
            {
                Endpoint = g.Key,
                Baseline = g.FirstOrDefault(r => r.Version == baseline).Stats,
                Current = g.FirstOrDefault(r => r.Version == current).Stats,
            })
            .OrderByDescending(e => (e.Baseline?.Count ?? 0) + (e.Current?.Count ?? 0))
            .ToList();

    private async Task<List<T>> ReadAsync<T>(VersionComparisonSql query, Func<System.Data.Common.DbDataReader, T> map, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await using var reader = await client.ExecuteReaderAsync(query.Sql, query.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
