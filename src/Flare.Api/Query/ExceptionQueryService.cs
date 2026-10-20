using Flare.Api.Errors;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IExceptionQueryService
{
    Task<ExceptionGroupsResponse> GetGroupsAsync(ExceptionGroupsRequest request, CancellationToken cancellationToken);

    Task<ExceptionOccurrencesResponse> GetOccurrencesAsync(ExceptionOccurrencesRequest request, CancellationToken cancellationToken);

    Task<ExceptionFacetValuesResponse> GetFacetValuesAsync(ExceptionFacetValuesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the <c>/errors</c> page -
/// same role/style as <see cref="SpanQueryService"/>/<see cref="ServiceOverviewQueryService"/>
/// (manual <c>ExecuteReaderAsync</c> + ordinal reads, not <c>QueryAsync&lt;T&gt;</c> - see
/// <see cref="LogQueryService"/>'s remarks for why), deliberately its own sibling class rather
/// than folded into <see cref="SpanQueryService"/> - the read shapes (an <c>ARRAY JOIN</c>'d
/// aggregate vs. a whole-span search) diverge enough to cost more shared-code complexity than
/// they'd save.
/// </summary>
public sealed class ExceptionQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider, IErrorIssueQueryService errorIssues, IErrorOwnershipQueryService ownership, IErrorGroupingQueryService grouping) : IExceptionQueryService
{
    public async Task<ExceptionGroupsResponse> GetGroupsAsync(ExceptionGroupsRequest request, CancellationToken cancellationToken)
    {
        var built = ExceptionGroupQueryBuilder.Build(request, timeProvider.GetUtcNow(), await grouping.GetAsync(cancellationToken));

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var groups = new List<ExceptionGroup>();
        while (reader.Read())
        {
            groups.Add(new ExceptionGroup
            {
                ExceptionType = reader.GetString(0),
                ExceptionMessage = reader.GetString(1),
                OccurrenceCount = reader.GetFieldValue<ulong>(2),
                FirstSeen = ReadUtc(reader, 3),
                LastSeen = ReadUtc(reader, 4),
                AffectedServices = reader.GetFieldValue<string[]>(5),
                SamplePaths = StackFramePaths.Extract(reader.GetString(6)),
            });
        }

        return new ExceptionGroupsResponse { Groups = await WithOwnersAsync(groups, cancellationToken) };
    }

    /// <summary>Fills each group's <see cref="ExceptionGroup.Owner"/> (ADR-0197). Best-effort: a failed lookup leaves the groups unowned.</summary>
    private async Task<IReadOnlyList<ExceptionGroup>> WithOwnersAsync(List<ExceptionGroup> groups, CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
        {
            return groups;
        }

        try
        {
            var assignees = await errorIssues.ListAssigneesAsync(cancellationToken);
            var rules = await ownership.ListAsync(cancellationToken);
            return groups.Select(g =>
            {
                assignees.TryGetValue(ErrorIssueFingerprint.Key(g.ExceptionType, g.ExceptionMessage), out var assignee);
                var owner = ErrorOwnership.Resolve(assignee, rules, g.ExceptionType, g.AffectedServices, g.SamplePaths);
                return owner is null ? g : g with { Owner = owner.Owner };
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return groups;
        }
    }

    public async Task<ExceptionOccurrencesResponse> GetOccurrencesAsync(ExceptionOccurrencesRequest request, CancellationToken cancellationToken)
    {
        var built = ExceptionOccurrenceQueryBuilder.Build(request, timeProvider.GetUtcNow(), await grouping.GetAsync(cancellationToken));

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var occurrences = new List<ExceptionOccurrence>();
        while (reader.Read())
        {
            occurrences.Add(new ExceptionOccurrence
            {
                TraceId = reader.GetString(0),
                SpanId = reader.GetString(1),
                ServiceName = reader.GetString(2),
                SpanName = reader.GetString(3),
                Timestamp = ReadUtc(reader, 4),
                Stacktrace = reader.GetString(5),
                Revision = reader.GetString(6),
                ServiceVersion = reader.GetString(7),
                Message = reader.GetString(8),
            });
        }

        return new ExceptionOccurrencesResponse
        {
            ExceptionType = request.ExceptionType,
            ExceptionMessage = request.ExceptionMessage,
            Occurrences = occurrences,
        };
    }

    public async Task<ExceptionFacetValuesResponse> GetFacetValuesAsync(ExceptionFacetValuesRequest request, CancellationToken cancellationToken)
    {
        var built = ExceptionFacetValuesQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var values = new List<ExceptionFacetValue>();
        while (reader.Read())
        {
            values.Add(new ExceptionFacetValue { Value = reader.GetString(0), Count = (long)reader.GetFieldValue<ulong>(1) });
        }

        return new ExceptionFacetValuesResponse { Values = values };
    }

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>/<see cref="SpanQueryService"/>'s own <c>ReadUtc</c> - <c>Flare.Ingest</c> always writes UTC wall-clock values.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same scan/time safety cap as <see cref="SpanQueryService.SafetyOptions"/>.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
