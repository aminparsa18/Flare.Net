using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for one of the <c>/llm</c> page's queries, ready to hand to <see cref="LlmQueryService"/>.</summary>
public sealed record LlmSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for the <c>/llm</c> page (<c>POST /api/llm/models</c>) - model calls
/// grouped by provider and model, derived from spans' OTel GenAI <c>gen_ai.*</c> attributes at
/// query time (ADR-0100), or from the <c>llm_model_calls</c> rollup when enabled (ADR-0102).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which spans count.</b> Model calls only (<see cref="ModelCallCondition"/>): an operation
/// of <c>chat</c>/<c>text_completion</c>/<c>generate_content</c>/<c>embeddings</c>, or no
/// operation but a model. Agent and tool spans carry a model too, and some instrumentations
/// roll their children's tokens up into them, so counting them would double the token totals.
/// The <c>mapContains</c> guard in <see cref="SpanWhere"/> lets <c>idx_span_attr_key</c>
/// (0007_spans.sql) skip granules with no GenAI spans at all.
/// </para>
/// <para>
/// <b>Attribute fallbacks.</b> Each expression reads the current semantic-convention name
/// first and the older one second (<see cref="ProviderExpr"/>, <see cref="ModelExpr"/>,
/// <see cref="InputTokensExpr"/>, <see cref="OutputTokensExpr"/>). Token attributes are strings
/// in <c>SpanAttributes</c>, hence <c>toUInt64OrZero</c>.
/// </para>
/// </remarks>
public static class LlmQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    /// <summary>Row cap for the model list. Fetches one more so the caller could tell it was cut.</summary>
    public const int MaxRows = 500;

    public const string ProviderExpr =
        "if(SpanAttributes['gen_ai.provider.name'] != '', SpanAttributes['gen_ai.provider.name'], SpanAttributes['gen_ai.system'])";

    /// <summary>The request model (what the code asked for), else the response model - which is often a dated snapshot name that would split one model into several rows.</summary>
    public const string ModelExpr =
        "if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model'])";

    public const string InputTokensExpr =
        "toUInt64OrZero(if(SpanAttributes['gen_ai.usage.input_tokens'] != '', SpanAttributes['gen_ai.usage.input_tokens'], SpanAttributes['gen_ai.usage.prompt_tokens']))";

    public const string OutputTokensExpr =
        "toUInt64OrZero(if(SpanAttributes['gen_ai.usage.output_tokens'] != '', SpanAttributes['gen_ai.usage.output_tokens'], SpanAttributes['gen_ai.usage.completion_tokens']))";

    /// <summary>A span that is one call to a model - see the class remarks.</summary>
    public const string ModelCallCondition =
        "(SpanAttributes['gen_ai.operation.name'] IN ('chat', 'text_completion', 'generate_content', 'embeddings') " +
        $"OR (SpanAttributes['gen_ai.operation.name'] = '' AND {ModelExpr} != ''))";

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    /// <summary>Same epoch-ms end convention as <see cref="HostInventoryQueryBuilder.ResolveWindowEnd"/>.</summary>
    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        HostInventoryQueryBuilder.ResolveWindowEnd(endUnixMs, now);

    /// <summary>
    /// One row per (provider, model). Columns: Provider, Model, CallCount, ErrorCount, Quantiles
    /// (p50/p95/p99 nanoseconds), InputTokens, OutputTokens, ServiceCount, LastSeenUnixMs.
    /// </summary>
    public static LlmSql BuildModels(LlmModelsRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var sql = "SELECT\n" +
            "    LlmProvider,\n" +
            "    LlmModel,\n" +
            "    sum(SampleWeight) AS CallCount,\n" +
            "    sumIf(SampleWeight, IsError) AS ErrorCount,\n" +
            "    quantilesTDigestWeighted(0.5, 0.95, 0.99)(DurationNano, SampleWeight) AS Quantiles,\n" +
            "    sum(InputTokens * SampleWeight) AS InputTokens,\n" +
            "    sum(OutputTokens * SampleWeight) AS OutputTokens,\n" +
            "    uniqExact(ServiceName) AS ServiceCount,\n" +
            "    toUnixTimestamp64Milli(max(StartTime)) AS LastSeenUnixMs\n" +
            $"FROM {SpanSource(where)}\n" +
            "GROUP BY LlmProvider, LlmModel\n" +
            "ORDER BY CallCount DESC, LlmModel\n" +
            "LIMIT {limit:UInt32}";

        return new LlmSql(sql, parameters);
    }

    /// <summary>
    /// Every service that made a model call in the window, as one sorted array in one row - the
    /// toolbar's service picker. Ignores the request's own service filter so picking one doesn't
    /// hide the others.
    /// </summary>
    public static LlmSql BuildFacets(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, service: null);

        var sql = "SELECT arraySort(groupUniqArray(1000)(ServiceName)) AS Services\n" +
            $"FROM {SpanSource(where)}";

        return new LlmSql(sql, parameters);
    }

    /// <summary>
    /// Same result shape as <see cref="BuildModels"/>, read from the flush-time <c>llm_model_calls</c>
    /// rollup (0047_llm_model_calls.sql, ADR-0102) instead of scanning <c>spans</c>. The window
    /// is floored/ceiled to whole minutes, the same bounded edge over-inclusion as ADR-0030.
    /// </summary>
    public static LlmSql BuildModelsFromRollup(LlmModelsRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = RollupWhere(parameters, windowMinutes, end, request.Service);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var sql = "SELECT\n" +
            "    Provider AS LlmProvider,\n" +
            "    Model AS LlmModel,\n" +
            "    sum(CallCount) AS CallCount,\n" +
            "    sum(ErrorCount) AS ErrorCount,\n" +
            $"    {SampledQuantileSql.MergeMany("0.5, 0.95, 0.99", "QuantileState", "QuantileWState")} AS Quantiles,\n" +
            "    sum(InputTokens) AS InputTokens,\n" +
            "    sum(OutputTokens) AS OutputTokens,\n" +
            "    uniqExact(ServiceName) AS ServiceCount,\n" +
            "    toUnixTimestamp64Milli(max(LastSeen)) AS LastSeenUnixMs\n" +
            "FROM llm_model_calls\n" +
            $"WHERE {where}\n" +
            "GROUP BY LlmProvider, LlmModel\n" +
            "ORDER BY CallCount DESC, LlmModel\n" +
            "LIMIT {limit:UInt32}";

        return new LlmSql(sql, parameters);
    }

    /// <summary>Rollup counterpart of <see cref="BuildFacets"/>.</summary>
    public static LlmSql BuildFacetsFromRollup(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = RollupWhere(parameters, windowMinutes, end, service: null);

        var sql = "SELECT arraySort(groupUniqArray(1000)(ServiceName)) AS Services\n" +
            "FROM llm_model_calls\n" +
            $"WHERE {where}";

        return new LlmSql(sql, parameters);
    }

    private static string RollupWhere(ClickHouseParameterCollection parameters, int windowMinutes, DateTimeOffset end, string? service)
    {
        parameters.AddParameter("from", FloorToMinute(end.AddMinutes(-windowMinutes).UtcDateTime));
        parameters.AddParameter("to", CeilToMinute(end.UtcDateTime));

        var clauses = new List<string> { "TimeBucket >= {from:DateTime}", "TimeBucket < {to:DateTime}" };

        if (!string.IsNullOrWhiteSpace(service))
        {
            parameters.AddParameter("service", service);
            clauses.Add("ServiceName = {service:String}");
        }

        return string.Join(" AND ", clauses);
    }

    private static DateTime FloorToMinute(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

    private static DateTime CeilToMinute(DateTime value)
    {
        var floored = FloorToMinute(value);
        return floored == value ? floored : floored.AddMinutes(1);
    }

    /// <summary>The per-span projection every query here aggregates over; the trailing <c>WHERE</c> is the inner query's.</summary>
    private static string SpanSource(string where) =>
        "(\n" +
        "    SELECT\n" +
        "        ServiceName,\n" +
        "        StartTime,\n" +
        "        DurationNano,\n" +
        "        SampleWeight,\n" +
        "        StatusCode = 'STATUS_CODE_ERROR' AS IsError,\n" +
        $"        {ProviderExpr} AS LlmProvider,\n" +
        $"        {ModelExpr} AS LlmModel,\n" +
        $"        {InputTokensExpr} AS InputTokens,\n" +
        $"        {OutputTokensExpr} AS OutputTokens\n" +
        "    FROM spans\n" +
        $"    WHERE {where}\n" +
        ")";

    private static string SpanWhere(ClickHouseParameterCollection parameters, int windowMinutes, DateTimeOffset end, string? service)
    {
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
            "(mapContains(SpanAttributes, 'gen_ai.operation.name') OR mapContains(SpanAttributes, 'gen_ai.request.model') " +
                "OR mapContains(SpanAttributes, 'gen_ai.response.model'))",
            ModelCallCondition,
        };

        if (!string.IsNullOrWhiteSpace(service))
        {
            parameters.AddParameter("service", service);
            clauses.Add("ServiceName = {service:String}");
        }

        return string.Join(" AND ", clauses);
    }
}
