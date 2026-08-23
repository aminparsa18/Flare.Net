using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface ISpanQueryService
{
    Task<SpanSearchResponse> SearchAsync(SpanSearchRequest request, CancellationToken cancellationToken);

    /// <summary>Every span sharing <paramref name="traceId"/>, for the waterfall view. Returns <see langword="null"/> if no spans match.</summary>
    Task<TraceDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for spans - same role as
/// <see cref="LogQueryService"/>, deliberately a separate sibling class rather than a
/// shared/generic one (the read shapes diverge enough - Nested array columns, a
/// different cursor tuple - that forcing a shared base would cost more clarity than it
/// saves for two implementations).
/// </summary>
/// <remarks>
/// Same <c>ExecuteReaderAsync</c> + manual <see cref="ClickHouseDataReader"/> ordinal
/// reads as <see cref="LogQueryService"/>, not <c>QueryAsync&lt;T&gt;</c> - see that
/// class's remarks for why. The <c>Events</c> Nested column's three desugared array
/// columns read back as plain <c>DateTime[]</c>/<c>string[]</c>/
/// <c>Dictionary&lt;string,string&gt;[]</c> via <c>GetFieldValue&lt;T&gt;</c> - confirmed
/// by the same live spike that de-risked the write side (see
/// <c>Flare.Ingest</c>'s <c>ClickHouseSpanRowMapper</c> remarks) and re-confirmed against
/// real inserted data during this roadmap slice's Pass 2 gate.
///
/// <para>
/// <paramref name="clusterMode"/> (same <c>ClickHouse:ClusterMode</c> flag
/// <c>ClickHouseMigrationRunner</c>/<c>IndexingQueryService</c> read - see
/// docs/clustering.md) only affects <see cref="GetTraceAsync"/> - see
/// <see cref="TraceByIdQueryOptions"/>.
/// </para>
/// </remarks>
public sealed class SpanQueryService(IClickHouseClient client, TimeProvider timeProvider, bool clusterMode) : ISpanQueryService
{
    public async Task<SpanSearchResponse> SearchAsync(SpanSearchRequest request, CancellationToken cancellationToken)
    {
        var built = SpanSearchQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var rows = new List<SpanDto>();
        while (reader.Read())
        {
            rows.Add(ReadSpan(reader));
        }

        // A PageSize+1'th row means another page exists - same trim-and-derive-cursor
        // trick as LogQueryService.SearchAsync.
        var hasMore = rows.Count > built.PageSize;
        var spans = hasMore ? rows.GetRange(0, built.PageSize) : rows;
        var nextCursor = hasMore
            ? new SpanSearchCursor(spans[^1].StartTime, spans[^1].TraceId, spans[^1].SpanId).Encode()
            : null;

        // Root-span search doubles as Flare's "trace list" view (see SpanDto.SpanCount's
        // remarks) - only that mode needs a count, so only that mode pays for the
        // follow-up query.
        if (request.Filter is { RootSpansOnly: true } && spans.Count > 0)
        {
            spans = await WithSpanCountsAsync(spans, cancellationToken);
        }

        return new SpanSearchResponse { Spans = spans, NextCursor = nextCursor };
    }

    private async Task<List<SpanDto>> WithSpanCountsAsync(List<SpanDto> roots, CancellationToken cancellationToken)
    {
        var built = SpanCountQueryBuilder.Build(roots.Select(r => r.TraceId));

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var counts = new Dictionary<string, ulong>();
        while (reader.Read())
        {
            counts[reader.GetString(0)] = reader.GetFieldValue<ulong>(1);
        }

        // A trace id absent from `counts` would mean its own root span vanished between
        // the two queries (a real, if narrow, race with concurrent writes) - falls back
        // to 1 (itself) rather than null, since "we already know at least this root span
        // exists" is still true.
        return roots.ConvertAll(r => r with { SpanCount = counts.GetValueOrDefault(r.TraceId, 1UL) });
    }

    public async Task<TraceDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        var built = TraceByIdQueryBuilder.Build(traceId);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, TraceByIdQueryOptions(), cancellationToken);

        var spans = new List<SpanDto>();
        while (reader.Read())
        {
            spans.Add(ReadSpan(reader));
        }

        return spans.Count == 0 ? null : new TraceDto { TraceId = traceId, Spans = spans };
    }

    private static SpanDto ReadSpan(ClickHouseDataReader reader)
    {
        var eventTimes = reader.GetFieldValue<DateTime[]>(19);
        var eventNames = reader.GetFieldValue<string[]>(20);
        var eventAttributes = reader.GetFieldValue<Dictionary<string, string>[]>(21);

        var events = new List<SpanEventDto>(eventTimes.Length);
        for (var i = 0; i < eventTimes.Length; i++)
        {
            events.Add(new SpanEventDto
            {
                Timestamp = new DateTimeOffset(DateTime.SpecifyKind(eventTimes[i], DateTimeKind.Utc)),
                Name = eventNames[i],
                Attributes = eventAttributes[i],
            });
        }

        return new SpanDto
        {
            TraceId = reader.GetString(0),
            SpanId = reader.GetString(1),
            ParentSpanId = reader.GetString(2),
            TraceState = reader.GetString(3),
            Name = reader.GetString(4),
            Kind = reader.GetByte(5),
            StartTime = ReadUtc(reader, 6),
            EndTime = ReadUtc(reader, 7),
            DurationNano = reader.GetFieldValue<ulong>(8),
            StatusCode = reader.GetString(9),
            StatusMessage = reader.GetString(10),
            ServiceName = reader.GetString(11),
            ResourceSchemaUrl = reader.GetString(12),
            ResourceAttributes = reader.GetFieldValue<Dictionary<string, string>>(13),
            ScopeSchemaUrl = reader.GetString(14),
            ScopeName = reader.GetString(15),
            ScopeVersion = reader.GetString(16),
            ScopeAttributes = reader.GetFieldValue<Dictionary<string, string>>(17),
            SpanAttributes = reader.GetFieldValue<Dictionary<string, string>>(18),
            Events = events,
        };
    }

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c> - <c>Flare.Ingest</c> always writes UTC wall-clock values.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same scan/time safety cap as <see cref="LogQueryService.SafetyOptions"/>.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };

    /// <summary>
    /// <see cref="SafetyOptions"/> plus, only under cluster mode,
    /// <c>optimize_skip_unused_shards</c> - lets ClickHouse skip the shard that provably
    /// can't hold <c>traceId</c> instead of fanning the query out to every shard and
    /// merging, now that <c>spans</c> shards on <c>cityHash64(TraceId)</c> rather than
    /// <c>rand()</c> (see <c>db/clickhouse-cluster/0007_spans.sql</c> and
    /// docs/clustering.md's "Design decision" section).
    /// <para>
    /// Deliberately best-effort, not forced - <c>force_optimize_skip_unused_shards</c>
    /// stays unset. If ClickHouse can't determine which shard holds this trace for any
    /// reason, this setting alone just falls back to querying every shard, same as
    /// before <c>cityHash64(TraceId)</c> existed; forcing it would instead turn that same
    /// situation into a hard error on a routine trace-by-id lookup the dashboard's
    /// waterfall view depends on - a strictly worse failure mode than "no speedup this
    /// time."
    /// </para>
    /// <para>
    /// Relies on every row in <c>spans_local</c> actually being routed by
    /// <c>cityHash64(TraceId)</c> - true for anything inserted through the <c>spans</c>
    /// Distributed table since that sharding key was set, but NOT true for rows a cluster
    /// inserted under the old <c>rand()</c> key before that change (they'd sit on
    /// whichever shard <c>rand()</c> happened to pick, not the shard
    /// <c>cityHash64(TraceId)</c> would now compute for the same TraceId) - skipping a
    /// shard for such a trace would silently omit its older spans rather than error.
    /// Same "fresh volumes only, not a live migration path" posture cluster mode already
    /// has elsewhere (see docs/clustering.md) - not a new risk this introduces, but worth
    /// restating here since this is the one place that risk turns into a live query
    /// behavior instead of just a schema definition.
    /// </para>
    /// <para>
    /// Confirmed live (2026-08-23) against a real 4-node cluster: ran this exact query
    /// with and without <c>optimize_skip_unused_shards</c> and checked
    /// <c>system.query_log</c> across all 4 nodes - without it, a trace-by-id lookup
    /// forwards a sub-query to the shard that holds none of that trace's data; with it,
    /// that shard shows no query_log entry at all, and the query still returns the
    /// correct rows. Verified in both directions (a shard-1 trace and a shard-2 trace).
    /// </para>
    /// </summary>
    private QueryOptions TraceByIdQueryOptions()
    {
        var options = SafetyOptions();
        if (clusterMode)
        {
            // Non-null: SafetyOptions() above always populates CustomSettings itself.
            options.CustomSettings!["optimize_skip_unused_shards"] = 1;
        }

        return options;
    }
}
