using ClickHouse.Driver;
using Flare.Api.Query;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Flare.Api.Loki;

/// <summary>Index statistics for <c>index/stats</c>.</summary>
internal sealed record LokiStats(ulong Streams, ulong Entries, ulong Bytes);

/// <summary>
/// The one place the Loki-compatible API (ADR-0236) talks to ClickHouse. Everything upstream
/// (<see cref="LokiQlParser"/>, <see cref="LokiSqlContext"/>, <see cref="LokiQueries"/>,
/// <see cref="LokiEvaluator"/>, <see cref="LokiEntryLabels"/>) is pure and unit-tested; this class only
/// runs the SQL they build under the shared query caps and reads rows back.
/// </summary>
/// <remarks>
/// Label names are mapped to attribute keys through <see cref="LokiLabelResolver"/>, which needs the keys
/// present in the window: they are sampled once per window and cached for a minute, per service scope so a
/// scoped token never sees another scope's key names.
/// </remarks>
internal sealed class LokiQueryService(
    IClickHouseClient client,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider,
    IPromotedAttributeRegistry promotedAttributes,
    IMemoryCache cache)
{
    private static readonly TimeSpan ResolverCacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>Key discovery looks at least this far back, so a query over a short window still resolves labels.</summary>
    private static readonly TimeSpan MinDiscoveryWindow = TimeSpan.FromHours(1);

    /// <summary>A leaf source bound to one request's window and label resolver.</summary>
    public ILokiLeafSource LeafSource(LokiLabelResolver resolver) => new RequestLeafSource(this, resolver);

    public async Task<LokiLabelResolver> ResolverAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var discoveryFrom = from < to - MinDiscoveryWindow ? from : to - MinDiscoveryWindow;
        var scope = string.Join(',', ServiceScope.Current ?? []);
        var key = $"loki-keys:{scope}:{discoveryFrom.ToUnixTimeSeconds() / 300}:{to.ToUnixTimeSeconds() / 300}";
        if (cache.TryGetValue(key, out LokiLabelResolver? cached) && cached is not null)
        {
            return cached;
        }

        var context = new LokiSqlContext(LokiLabelResolver.Empty, discoveryFrom, to, timeProvider.GetUtcNow(), promotedAttributes.Logs);
        var built = LokiQueries.AttributeKeys(context);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var resource = new List<string>();
        var log = new List<string>();
        while (reader.Read())
        {
            var tagged = reader.GetString(0);
            (tagged.StartsWith("r:", StringComparison.Ordinal) ? resource : log).Add(tagged[2..]);
        }

        var resolver = new LokiLabelResolver(resource, log);
        cache.Set(key, resolver, ResolverCacheTtl);
        return resolver;
    }

    /// <summary>Log lines for a selector, grouped by the caller into streams. Newest first unless <paramref name="forward"/>.</summary>
    public async Task<IReadOnlyList<LokiLogEntry>> QueryLogsAsync(
        LokiLogSelector selector, DateTimeOffset from, DateTimeOffset to, int limit, bool forward, CancellationToken cancellationToken)
    {
        var resolver = await ResolverAsync(from, to, cancellationToken);
        var context = new LokiSqlContext(resolver, from, to, timeProvider.GetUtcNow(), promotedAttributes.Logs);
        context.AddSelector(selector);
        var built = LokiQueries.LogLines(context, limit, forward);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
        var entries = new List<LokiLogEntry>();
        while (reader.Read())
        {
            var row = new LokiLogRow(
                reader.GetFieldValue<long>(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetByte(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetFieldValue<Dictionary<string, string>>(9),
                reader.GetFieldValue<Dictionary<string, string>>(10));
            entries.Add(LokiEntryLabels.Build(row, context.Json));
        }

        return entries;
    }

    public async Task<LokiMatrix> EvaluateAsync(
        LokiExpr expr, DateTimeOffset start, DateTimeOffset end, TimeSpan step, bool instant, CancellationToken cancellationToken)
    {
        var resolver = await ResolverAsync(start, end, cancellationToken);
        var evaluator = new LokiEvaluator(LeafSource(resolver));
        return await evaluator.EvaluateAsync(expr, start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds(), (long)step.TotalMilliseconds, instant, cancellationToken);
    }

    /// <summary>Label names for <c>/labels</c>: the fixed ones plus the resource attributes seen in the window.</summary>
    public async Task<IReadOnlyList<string>> LabelsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        (await ResolverAsync(from, to, cancellationToken)).SelectorLabelNames();

    public async Task<IReadOnlyList<string>> LabelValuesAsync(
        string label, LokiLogSelector? selector, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var resolver = await ResolverAsync(from, to, cancellationToken);
        var context = new LokiSqlContext(resolver, from, to, timeProvider.GetUtcNow(), promotedAttributes.Logs);
        if (selector is not null)
        {
            context.AddSelector(selector);
        }

        var built = LokiQueries.LabelValues(context, label, LokiQueries.DefaultMetadataLimit);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>The distinct stream label sets of the streams matching any of <paramref name="selectors"/> (all streams when none).</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> SeriesAsync(
        IReadOnlyList<LokiLogSelector> selectors, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var resolver = await ResolverAsync(from, to, cancellationToken);
        var dimensions = resolver.StreamLabelNames();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IReadOnlyDictionary<string, string>>();

        foreach (var selector in selectors.Count == 0 ? [null] : selectors.Select(s => (LokiLogSelector?)s))
        {
            var context = new LokiSqlContext(resolver, from, to, timeProvider.GetUtcNow(), promotedAttributes.Logs);
            if (selector is not null)
            {
                context.AddSelector(selector);
            }

            var built = LokiQueries.Streams(context, dimensions, LokiQueries.DefaultMetadataLimit);
            await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
            while (reader.Read())
            {
                var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < dimensions.Count; i++)
                {
                    if (reader.GetString(i) is { Length: > 0 } value)
                    {
                        labels[dimensions[i]] = value;
                    }
                }

                if (seen.Add(string.Join('\u0001', labels.Select(kv => kv.Key + "=" + kv.Value))))
                {
                    result.Add(labels);
                }
            }
        }

        return result;
    }

    public async Task<LokiStats> StatsAsync(LokiLogSelector selector, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var resolver = await ResolverAsync(from, to, cancellationToken);
        var context = new LokiSqlContext(resolver, from, to, timeProvider.GetUtcNow(), promotedAttributes.Logs);
        context.AddSelector(selector);
        var built = LokiQueries.Stats(context);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
        return reader.Read()
            ? new LokiStats(Streams: reader.GetFieldValue<ulong>(2), Entries: reader.GetFieldValue<ulong>(0), Bytes: reader.GetFieldValue<ulong>(1))
            : new LokiStats(0, 0, 0);
    }

    private IClickHouseClient Client => client;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private PromotedAttributeColumns? Promoted => promotedAttributes.Logs;

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);

    private sealed class RequestLeafSource(LokiQueryService service, LokiLabelResolver resolver) : ILokiLeafSource
    {
        public Task<IReadOnlyList<string>> StreamLabelNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(resolver.StreamLabelNames());

        public async Task<IReadOnlyList<LokiBucketRow>> FetchBucketsAsync(LokiLeafRequest request, CancellationToken cancellationToken)
        {
            var context = new LokiSqlContext(
                resolver,
                DateTimeOffset.FromUnixTimeMilliseconds(request.FromMs),
                DateTimeOffset.FromUnixTimeMilliseconds(request.ToMs),
                service.Now,
                service.Promoted);
            context.AddSelector(request.Selector);
            var built = LokiQueries.Buckets(context, request.Dimensions, request.Measure == LokiMeasure.Bytes, request.FromMs, request.BucketMs);

            await using var reader = await service.Client.ExecuteReaderAsync(built.Sql, built.Parameters, service.SafetyOptions(), cancellationToken);
            var rows = new List<LokiBucketRow>();
            while (reader.Read())
            {
                var values = new string[request.Dimensions.Count];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = reader.GetString(i + 1);
                }

                var bucketMs = request.BucketMs > 0 ? request.FromMs + reader.GetFieldValue<long>(0) * request.BucketMs : request.FromMs;
                rows.Add(new LokiBucketRow(bucketMs, values, reader.GetDouble(values.Length + 1)));
            }

            return rows;
        }
    }
}
