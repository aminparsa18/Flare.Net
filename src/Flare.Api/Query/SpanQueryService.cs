using System.Globalization;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ISpanQueryService
{
    Task<SpanSearchResponse> SearchAsync(SpanSearchRequest request, CancellationToken cancellationToken);

    /// <summary>Every span sharing <paramref name="traceId"/>, for the waterfall view. Returns <see langword="null"/> if no spans match.</summary>
    Task<TraceDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken);

    /// <summary>
    /// The next depth levels below one span of a trace loaded lazily (<see cref="TraceDto.Truncated"/>),
    /// merged by the caller into what it already holds.
    /// </summary>
    Task<TraceDto> GetSubtreeAsync(string traceId, string parentSpanId, CancellationToken cancellationToken);

    /// <summary>Every distinct value observed for one attribute key, most-observed first - the Attribute filters builder's value autocomplete. See <see cref="SpanAttributeValuesQueryBuilder"/>.</summary>
    Task<SpanAttributeValuesResponse> GetAttributeValuesAsync(SpanAttributeValuesRequest request, CancellationToken cancellationToken);

    /// <summary>The most common attribute names of one bag in a window, most-used first - the Tempo-compatible API's tag discovery. See <see cref="SpanAttributeKeysQueryBuilder"/>.</summary>
    Task<IReadOnlyList<string>> GetAttributeKeysAsync(SpanAttributeKeysRequest request, CancellationToken cancellationToken);

    /// <summary>For each given trace, its spans matching a structure's conditions - the Tempo-compatible API's <c>spanSets</c>. See <see cref="TraceMatchedSpansQueryBuilder"/>.</summary>
    Task<IReadOnlyList<TraceMatchedSpans>> GetMatchedSpansAsync(MatchedSpansRequest request, CancellationToken cancellationToken);

    /// <summary>A TraceQL metrics query's per-step, per-group aggregates over the matching spans - the Tempo-compatible API's <c>/api/metrics</c> (ADR-0221). See <see cref="TraceQlMetricsQueryBuilder"/>.</summary>
    Task<IReadOnlyList<SpanMetricRow>> GetSpanMetricsAsync(SpanMetricsRequest request, CancellationToken cancellationToken);

    /// <summary>A <c>compare()</c> query's per-step counts of every attribute value in the selection and the baseline (ADR-0226). See <see cref="TraceQlMetricsQueryBuilder.BuildCompare"/>.</summary>
    Task<IReadOnlyList<SpanCompareRow>> GetSpanCompareAsync(SpanMetricsRequest request, CancellationToken cancellationToken);

    /// <summary>The span with the largest value per step and group of a metrics query, to link a chart point to a trace (ADR-0223). See <see cref="TraceQlMetricsQueryBuilder.BuildExemplars"/>.</summary>
    Task<IReadOnlyList<SpanExemplarRow>> GetSpanExemplarsAsync(SpanMetricsRequest request, CancellationToken cancellationToken);

    /// <summary>Where one span's duration ranks among same service + name spans around it - see <see cref="SpanDurationPercentileQueryBuilder"/>.</summary>
    Task<SpanDurationPercentileResponse> GetDurationPercentileAsync(SpanDurationPercentileRequest request, CancellationToken cancellationToken);
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
public sealed class SpanQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider, IPromotedAttributeRegistry promotedAttributes, bool clusterMode) : ISpanQueryService
{
    public async Task<SpanSearchResponse> SearchAsync(SpanSearchRequest request, CancellationToken cancellationToken)
    {
        var built = SpanSearchQueryBuilder.Build(request, timeProvider.GetUtcNow(), promotedAttributes.Spans);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        // A SpanCount sort already joined the rollup in (see SpanSearchQueryBuilder's
        // remarks) - its two trailing columns fill SpanCount/HasError directly.
        var sortBy = Enum.IsDefined(request.SortBy) ? request.SortBy : SpanSortKey.StartTime;
        var rows = new List<SpanDto>();
        while (reader.Read())
        {
            var span = ReadSpan(reader);
            if (sortBy == SpanSortKey.SpanCount)
            {
                var first = SpanColumns.Names.Count;
                span = span with { SpanCount = reader.GetFieldValue<ulong>(first), HasError = reader.GetByte(first + 1) != 0 };
            }

            rows.Add(span);
        }

        // A PageSize+1'th row means another page exists - same trim-and-derive-cursor
        // trick as LogQueryService.SearchAsync.
        var hasMore = rows.Count > built.PageSize;
        var spans = hasMore ? rows.GetRange(0, built.PageSize) : rows;
        var nextCursor = hasMore ? CursorFor(spans[^1], sortBy, request.SortAscending).Encode() : null;

        // Root-span search doubles as Flare's "trace list" view (see SpanDto.SpanCount's
        // remarks), and entry-span search is that same list scoped to each service's
        // requests - only those modes need the count/error rollup, so only they pay for
        // the follow-up query.
        if (sortBy != SpanSortKey.SpanCount && request.Filter is { RootSpansOnly: true } or { EntrySpansOnly: true } && spans.Count > 0)
        {
            spans = await WithRollupsAsync(spans, cancellationToken);
        }

        return new SpanSearchResponse { Spans = spans, NextCursor = nextCursor };
    }

    private static SpanSearchCursor CursorFor(SpanDto last, SpanSortKey sortBy, bool ascending)
    {
        var value = sortBy switch
        {
            SpanSortKey.Duration => last.DurationNano,
            SpanSortKey.SpanCount => last.SpanCount ?? 0,
            _ => (ulong)last.StartTime.UtcTicks,
        };
        return new SpanSearchCursor(sortBy, ascending, value, last.TraceId, last.SpanId);
    }

    private async Task<List<SpanDto>> WithRollupsAsync(List<SpanDto> roots, CancellationToken cancellationToken)
    {
        var built = SpanRollupQueryBuilder.Build(roots.Select(r => r.TraceId));

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var rollups = new Dictionary<string, (ulong SpanCount, bool HasError)>();
        while (reader.Read())
        {
            rollups[reader.GetString(0)] = (reader.GetFieldValue<ulong>(1), reader.GetByte(2) != 0);
        }

        // A trace id absent from `rollups` would mean its own root span vanished between
        // the two queries (a real, if narrow, race with concurrent writes) - falls back
        // to a count of 1 (itself) and no known error rather than null, since "we already
        // know at least this root span exists, and know its own status" is still true.
        return roots.ConvertAll(r =>
        {
            var (spanCount, hasError) = rollups.GetValueOrDefault(r.TraceId, (1UL, r.StatusCode == "STATUS_CODE_ERROR"));
            return r with { SpanCount = spanCount, HasError = hasError };
        });
    }

    public async Task<TraceDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        var spans = await ReadLevelAsync(TraceByIdQueryBuilder.Build(traceId), cancellationToken);

        if (spans.Count == 0)
        {
            return null;
        }

        // The query fetches MaxSpans + 1; the extra row only proves the cap was hit. Rather
        // than keep a cut-off-by-time slice (which orphans children), load by depth instead.
        if (spans.Count > TraceByIdQueryBuilder.MaxSpans)
        {
            return await LoadLevelsAsync(traceId, TraceLevelQueryBuilder.BuildRoots(traceId), cancellationToken);
        }

        return new TraceDto { TraceId = traceId, Spans = spans };
    }

    public Task<TraceDto> GetSubtreeAsync(string traceId, string parentSpanId, CancellationToken cancellationToken) =>
        LoadLevelsAsync(traceId, TraceLevelQueryBuilder.BuildChildren(traceId, [parentSpanId]), cancellationToken);

    /// <summary>
    /// Breadth-first by depth from <paramref name="firstLevel"/>, stopping before a level that
    /// would push past <see cref="TraceLevelQueryBuilder.LazyBudget"/> (the first level is
    /// always kept). Each level is fetched before deciding, so when a level is dropped its
    /// distinct <c>ParentSpanId</c>s are exactly the loaded spans that still have children -
    /// no separate child-count query (except when the dropped level hit the row cap, see below).
    /// </summary>
    private async Task<TraceDto> LoadLevelsAsync(string traceId, TraceByIdSql firstLevel, CancellationToken cancellationToken)
    {
        var spans = new List<SpanDto>();
        var partial = new List<string>();
        var level = firstLevel;
        var first = true;
        List<string> previousIds = [];

        while (true)
        {
            var next = await ReadLevelAsync(level, cancellationToken);
            if (next.Count == 0)
            {
                break;
            }

            if (!first && spans.Count + next.Count > TraceLevelQueryBuilder.LazyBudget)
            {
                // A level that hit the row cap is itself cut off, so its distinct parents would
                // miss some; fall back to every span of the previous level (the few childless
                // ones just come back empty when expanded).
                partial.AddRange(next.Count >= TraceByIdQueryBuilder.MaxSpans
                    ? previousIds
                    : next.Select(s => s.ParentSpanId).Distinct());
                break;
            }

            first = false;
            spans.AddRange(next);
            previousIds = next.Select(s => s.SpanId).ToList();
            level = TraceLevelQueryBuilder.BuildChildren(traceId, previousIds);
        }

        return new TraceDto { TraceId = traceId, Spans = spans, Truncated = true, PartialSpanIds = partial };
    }

    private async Task<List<SpanDto>> ReadLevelAsync(TraceByIdSql built, CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, TraceByIdQueryOptions(), cancellationToken);
        var spans = new List<SpanDto>();
        while (reader.Read())
        {
            spans.Add(ReadSpan(reader));
        }

        return spans;
    }

    public async Task<SpanAttributeValuesResponse> GetAttributeValuesAsync(SpanAttributeValuesRequest request, CancellationToken cancellationToken)
    {
        var built = SpanAttributeValuesQueryBuilder.Build(request, timeProvider.GetUtcNow(), promotedAttributes.Spans);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var values = new List<SpanAttributeValueInfo>();
        while (reader.Read())
        {
            values.Add(new SpanAttributeValueInfo { Value = reader.GetString(0), Count = (long)reader.GetFieldValue<ulong>(1) });
        }

        return new SpanAttributeValuesResponse { Values = values };
    }

    public async Task<IReadOnlyList<string>> GetAttributeKeysAsync(SpanAttributeKeysRequest request, CancellationToken cancellationToken)
    {
        var built = SpanAttributeKeysQueryBuilder.Build(request, timeProvider.GetUtcNow(), promotedAttributes.Spans);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var keys = new List<string>();
        while (reader.Read())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task<IReadOnlyList<TraceMatchedSpans>> GetMatchedSpansAsync(MatchedSpansRequest request, CancellationToken cancellationToken)
    {
        if (request.TraceIds.Count == 0)
        {
            return [];
        }

        var built = TraceMatchedSpansQueryBuilder.Build(request, promotedAttributes.Spans);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var traces = new List<TraceMatchedSpans>();
        string? currentId = null;
        var current = new List<MatchedSpan>();
        var matched = 0;
        while (reader.Read())
        {
            var traceId = reader.GetString(0);
            if (traceId != currentId)
            {
                if (currentId is not null)
                {
                    traces.Add(new TraceMatchedSpans(currentId, matched, current));
                }

                currentId = traceId;
                current = [];
                matched = (int)Math.Min(reader.GetFieldValue<ulong>(6), int.MaxValue);
            }

            var attributes = TraceMatchedSpansQueryBuilder.MergeEchoed(
                reader.GetFieldValue<Dictionary<string, string>>(7), reader.GetFieldValue<Dictionary<string, string>>(8));
            current.Add(new MatchedSpan(reader.GetString(1), reader.GetString(2), reader.GetString(3), ReadUtc(reader, 4), reader.GetFieldValue<ulong>(5), attributes));
        }

        if (currentId is not null)
        {
            traces.Add(new TraceMatchedSpans(currentId, matched, current));
        }

        return traces;
    }

    public async Task<IReadOnlyList<SpanMetricRow>> GetSpanMetricsAsync(SpanMetricsRequest request, CancellationToken cancellationToken)
    {
        var built = TraceQlMetricsQueryBuilder.Build(request, promotedAttributes.Spans);

        // Steps x series is bounded by the builder, so lift the generic result-row cap to fit it and make an overflow an error:
        // 'break' would drop the last steps without saying so.
        var options = SafetyOptions();
        options.CustomSettings!["max_result_rows"] = Math.Max(queryLimits.Value.MaxResultRows, TraceQlMetricsQueryBuilder.MaxRowsFor(request.Function));
        options.CustomSettings["result_overflow_mode"] = "throw";

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, options, cancellationToken);

        var rows = new List<SpanMetricRow>();
        var groups = request.GroupBy.Count;
        var values = request.Function == SpanMetricFunction.Quantile ? request.Quantiles.Count : request.Function == SpanMetricFunction.Count ? 0 : 1; // Histogram: the bucket exponent
        while (reader.Read())
        {
            var group = new string[groups];
            for (var i = 0; i < groups; i++)
            {
                group[i] = reader.GetString(1 + i);
            }

            var weight = Convert.ToDouble(reader.GetValue(1 + groups), CultureInfo.InvariantCulture);
            var aggregate = new double[values == 0 ? 1 : values];
            if (values == 0)
            {
                aggregate[0] = weight;
            }
            else
            {
                for (var i = 0; i < values; i++)
                {
                    aggregate[i] = Convert.ToDouble(reader.GetValue(2 + groups + i), CultureInfo.InvariantCulture);
                }
            }

            rows.Add(new SpanMetricRow(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), group, weight, aggregate));
        }

        return rows;
    }

    public async Task<IReadOnlyList<SpanCompareRow>> GetSpanCompareAsync(SpanMetricsRequest request, CancellationToken cancellationToken)
    {
        var built = TraceQlMetricsQueryBuilder.BuildCompare(request, promotedAttributes.Spans);

        // One row per step, group, attribute and kept value; an overflow is an error, as for the other metrics queries, and so is a group by that
        // would not fit in memory (a span with thousands of distinct attribute values per step is what it protects against).
        var options = SafetyOptions();
        options.CustomSettings!["max_result_rows"] = Math.Max(queryLimits.Value.MaxResultRows, TraceQlMetricsQueryBuilder.MaxCompareRows);
        options.CustomSettings["result_overflow_mode"] = "throw";
        options.CustomSettings["max_rows_to_group_by"] = 20_000_000;
        options.CustomSettings["group_by_overflow_mode"] = "throw";

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, options, cancellationToken);

        var rows = new List<SpanCompareRow>();
        while (reader.Read())
        {
            rows.Add(new SpanCompareRow(
                Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != 0,
                reader.GetString(2),
                reader.GetString(3),
                Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<SpanExemplarRow>> GetSpanExemplarsAsync(SpanMetricsRequest request, CancellationToken cancellationToken)
    {
        var built = TraceQlMetricsQueryBuilder.BuildExemplars(request, promotedAttributes.Spans);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var rows = new List<SpanExemplarRow>();
        var groups = request.GroupBy.Count;
        while (reader.Read())
        {
            var group = new string[groups];
            for (var i = 0; i < groups; i++)
            {
                group[i] = reader.GetString(1 + i);
            }

            rows.Add(new SpanExemplarRow(
                Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                group,
                reader.GetString(1 + groups),
                reader.GetString(2 + groups),
                Convert.ToDouble(reader.GetValue(3 + groups), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(4 + groups), CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    public async Task<SpanDurationPercentileResponse> GetDurationPercentileAsync(SpanDurationPercentileRequest request, CancellationToken cancellationToken)
    {
        var built = SpanDurationPercentileQueryBuilder.Build(request);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        // An aggregate without GROUP BY always yields one row, even with no matches.
        if (!reader.Read())
        {
            return new SpanDurationPercentileResponse { SampleCount = 0, Percentile = 0, P50Nano = 0, P95Nano = 0, P99Nano = 0 };
        }

        var total = (long)reader.GetFieldValue<ulong>(0);
        var atOrBelow = (long)reader.GetFieldValue<ulong>(1);
        return new SpanDurationPercentileResponse
        {
            SampleCount = total,
            Percentile = total == 0 ? 0 : 100.0 * atOrBelow / total,
            P50Nano = SafeDouble(reader.GetDouble(2)),
            P95Nano = SafeDouble(reader.GetDouble(3)),
            P99Nano = SafeDouble(reader.GetDouble(4)),
        };
    }

    // quantile() over zero rows is NaN, which JSON can't carry.
    private static double SafeDouble(double value) => double.IsFinite(value) ? value : 0;

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

        var linkTraceIds = reader.GetFieldValue<string[]>(23);
        var linkSpanIds = reader.GetFieldValue<string[]>(24);
        var linkTraceStates = reader.GetFieldValue<string[]>(25);
        var linkAttributes = reader.GetFieldValue<Dictionary<string, string>[]>(26);

        var links = new List<SpanLinkDto>(linkTraceIds.Length);
        for (var i = 0; i < linkTraceIds.Length; i++)
        {
            links.Add(new SpanLinkDto
            {
                TraceId = linkTraceIds[i],
                SpanId = linkSpanIds[i],
                TraceState = linkTraceStates[i],
                Attributes = linkAttributes[i],
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
            IngestedAt = ReadUtc(reader, 22),
            Links = links,
        };
    }

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c> - <c>Flare.Ingest</c> always writes UTC wall-clock values.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same scan/time safety cap as <see cref="LogQueryService.SafetyOptions"/>.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);

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
