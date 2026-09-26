using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for one of the <c>/messaging</c> page's queries, ready to hand to <see cref="MessagingQueryService"/>.</summary>
public sealed record MessagingSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for the <c>/messaging</c> page (<c>POST /api/messaging/destinations</c>
/// and <c>POST /api/messaging/destination-detail</c>) - producer/consumer figures derived
/// from spans' OTel <c>messaging.*</c> attributes at query time, plus consumer lag from the
/// <c>kafka.consumer_group.lag</c> gauge. No new table; see
/// docs-internal/adr/0056-messaging-queue-monitoring.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which spans count.</b> Only spans with a <c>messaging.system</c> attribute - the
/// <c>mapContains</c> guard lets <c>idx_span_attr_key</c> (0007_spans.sql) skip granules with
/// no messaging spans at all, which is most of them. Each span is then classified by
/// <see cref="SpanRoleExpr"/>: the operation attribute (<c>messaging.operation.type</c>, else
/// the older <c>messaging.operation</c>) decides when present - <c>publish</c>/<c>create</c>/
/// <c>send</c> is a publish, <c>process</c>/<c>deliver</c> a process, <c>receive</c> a receive -
/// and only when neither is set does the span kind (4 = PRODUCER, 5 = CONSUMER, taken as a
/// process). Newer conventions emit send/receive spans as <c>CLIENT</c>, so kind alone would
/// drop them. Anything else (<c>settle</c>, internal spans that merely carry the attribute) is
/// excluded.
/// </para>
/// <para>
/// <b>Receive vs. process.</b> Both count as a consume, but not both for the same consumer:
/// OpenTelemetry.Instrumentation.ConfluentKafka emits a <c>receive</c> (poll) span <em>and</em>
/// a <c>process</c> span per message, which would double every consume count and mix fetch
/// time into handling latency. So <see cref="SpanSource"/> drops a consumer's receive spans
/// whenever that consumer - (system, destination, service, consumer group) - has any process
/// spans in the window, and keeps them only for receive-only consumers. Found by a live run
/// against a real broker; see ADR-0056.
/// </para>
/// <para>
/// <b>Attribute fallbacks.</b> Partition and consumer group read the current attribute name
/// first and the older Kafka-specific one second (<see cref="PartitionExpr"/>,
/// <see cref="ConsumerGroupExpr"/>) - .NET instrumentations in the wild still emit either.
/// </para>
/// <para>
/// Percentiles use <c>quantilesIf</c> (approximate, same tradeoff as every other percentile
/// here). They come back <c>nan</c> for a side with no spans; the service maps that to 0.
/// </para>
/// </remarks>
public static class MessagingQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    /// <summary>Row cap for the destinations list and each detail table. Fetches one more so the caller could tell it was cut.</summary>
    public const int MaxDestinations = 500;

    /// <summary>The OTel Collector <c>kafkametrics</c> receiver's per-partition lag gauge (attributes <c>group</c>, <c>topic</c>, <c>partition</c>).</summary>
    public const string ConsumerLagMetric = "kafka.consumer_group.lag";

    public const string PublishRole = "publish";
    public const string ConsumeRole = "consume";

    public const string SystemExpr = "SpanAttributes['messaging.system']";
    public const string DestinationExpr = "SpanAttributes['messaging.destination.name']";

    public const string OperationExpr =
        "if(SpanAttributes['messaging.operation.type'] != '', SpanAttributes['messaging.operation.type'], SpanAttributes['messaging.operation'])";

    public const string PartitionExpr =
        "if(SpanAttributes['messaging.destination.partition.id'] != '', SpanAttributes['messaging.destination.partition.id'], SpanAttributes['messaging.kafka.destination.partition'])";

    public const string ConsumerGroupExpr =
        "if(SpanAttributes['messaging.consumer.group.name'] != '', SpanAttributes['messaging.consumer.group.name'], SpanAttributes['messaging.kafka.consumer.group'])";

    /// <summary>Per-span role, before <see cref="SpanSource"/>'s receive-vs-process dedup collapses process/receive into <see cref="ConsumeRole"/>.</summary>
    public const string SpanRoleExpr =
        $"multiIf({OperationExpr} IN ('publish', 'create', 'send'), '{PublishRole}', " +
        $"{OperationExpr} IN ('process', 'deliver'), 'process', " +
        $"{OperationExpr} = 'receive', 'receive', " +
        $"{OperationExpr} = '' AND Kind = 4, '{PublishRole}', " +
        $"{OperationExpr} = '' AND Kind = 5, 'process', '')";

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    /// <summary>Same epoch-ms end convention as <see cref="HostInventoryQueryBuilder.ResolveWindowEnd"/>.</summary>
    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        HostInventoryQueryBuilder.ResolveWindowEnd(endUnixMs, now);

    /// <summary>One row per <c>(System, Destination)</c>: see <see cref="MessagingDestination"/> for the columns, in order.</summary>
    public static MessagingSql BuildDestinations(MessagingDestinationsRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.System, destination: null);
        parameters.AddParameter("limit", (uint)(MaxDestinations + 1));

        var sql = "SELECT\n" +
            "    MsgSystem,\n" +
            "    MsgDestination,\n" +
            $"    countIf(Role = '{PublishRole}') AS PublishCount,\n" +
            $"    countIf(Role = '{PublishRole}' AND IsError) AS PublishErrorCount,\n" +
            $"    quantilesIf(0.5, 0.99)(DurationNano, Role = '{PublishRole}') AS PublishQuantiles,\n" +
            $"    countIf(Role = '{ConsumeRole}') AS ConsumeCount,\n" +
            $"    countIf(Role = '{ConsumeRole}' AND IsError) AS ConsumeErrorCount,\n" +
            $"    quantilesIf(0.5, 0.99)(DurationNano, Role = '{ConsumeRole}') AS ConsumeQuantiles,\n" +
            $"    uniqExactIf(ServiceName, Role = '{PublishRole}') AS ProducerServiceCount,\n" +
            $"    uniqExactIf(ServiceName, Role = '{ConsumeRole}') AS ConsumerServiceCount,\n" +
            "    avg(BodySize) AS AvgMessageBytes\n" +
            $"FROM {SpanSource(where)}\n" +
            "GROUP BY MsgSystem, MsgDestination\n" +
            "ORDER BY PublishCount + ConsumeCount DESC, MsgSystem, MsgDestination\n" +
            "LIMIT {limit:UInt32}";

        return new MessagingSql(sql, parameters);
    }

    /// <summary>
    /// Every system and service with classified messaging spans in the window, as two sorted
    /// arrays in one row - the toolbar pickers. Deliberately ignores the request's own
    /// service/system filter so picking one doesn't hide the others.
    /// </summary>
    public static MessagingSql BuildFacets(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, service: null, system: null, destination: null);

        var sql = "SELECT\n" +
            "    arraySort(groupUniqArray(1000)(MsgSystem)) AS Systems,\n" +
            "    arraySort(groupUniqArray(1000)(ServiceName)) AS Services\n" +
            $"FROM {SpanSource(where)}";

        return new MessagingSql(sql, parameters);
    }

    /// <summary>
    /// One destination's producers and consumers, one row per <c>(Role, ServiceName,
    /// ConsumerGroup)</c> - <c>ConsumerGroup</c> is forced to <c>''</c> on publish rows so a
    /// producer that happens to carry a group attribute still collapses to one row. Columns:
    /// Role, ServiceName, ConsumerGroup, Count, ErrorCount, Quantiles, AvgMessageBytes.
    /// </summary>
    public static MessagingSql BuildServices(MessagingDestinationDetailRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.System, request.Destination);
        parameters.AddParameter("limit", (uint)(MaxDestinations + 1));

        var sql = "SELECT\n" +
            "    Role,\n" +
            "    ServiceName,\n" +
            $"    if(Role = '{PublishRole}', '', MsgGroup) AS ConsumerGroup,\n" +
            "    count() AS Count,\n" +
            "    countIf(IsError) AS ErrorCount,\n" +
            "    quantiles(0.5, 0.99)(DurationNano) AS Quantiles,\n" +
            "    avg(BodySize) AS AvgMessageBytes\n" +
            $"FROM {SpanSource(where)}\n" +
            "GROUP BY Role, ServiceName, ConsumerGroup\n" +
            "ORDER BY Count DESC, ServiceName, ConsumerGroup\n" +
            "LIMIT {limit:UInt32}";

        return new MessagingSql(sql, parameters);
    }

    /// <summary>One destination's traffic per partition. Columns: Partition, PublishCount, ConsumeCount, ErrorCount. Spans without a partition attribute are left out.</summary>
    public static MessagingSql BuildPartitions(MessagingDestinationDetailRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.System, request.Destination);
        parameters.AddParameter("limit", (uint)(MaxDestinations + 1));

        var sql = "SELECT\n" +
            "    MsgPartition,\n" +
            $"    countIf(Role = '{PublishRole}') AS PublishCount,\n" +
            $"    countIf(Role = '{ConsumeRole}') AS ConsumeCount,\n" +
            "    countIf(IsError) AS ErrorCount\n" +
            $"FROM {SpanSource(where)}\n" +
            "    AND MsgPartition != ''\n" +
            "GROUP BY MsgPartition\n" +
            "ORDER BY toUInt64OrNull(MsgPartition) ASC NULLS LAST, MsgPartition\n" +
            "LIMIT {limit:UInt32}";

        return new MessagingSql(sql, parameters);
    }

    /// <summary>
    /// Latest <see cref="ConsumerLagMetric"/> value in the window per <c>(topic, group,
    /// partition)</c>. With <paramref name="topic"/> set, one topic's rows (columns: Topic,
    /// ConsumerGroup, Partition, Lag); without it, summed per topic for the list (columns:
    /// Topic, Lag). <c>argMax(Value, Time)</c> rather than the window's max - lag is a
    /// level, and the question is where it stands now.
    /// </summary>
    public static MessagingSql BuildConsumerLag(int windowMinutes, DateTimeOffset end, string? topic)
    {
        var parameters = TimeParameters(windowMinutes, end);
        parameters.AddParameter("lagMetric", ConsumerLagMetric);
        parameters.AddParameter("limit", (uint)(MaxDestinations + 1));

        var topicClause = "";
        if (topic is not null)
        {
            parameters.AddParameter("topic", topic);
            topicClause = " AND DataPointAttributes['topic'] = {topic:String}";
        }

        var latest = "SELECT\n" +
            "        DataPointAttributes['topic'] AS Topic,\n" +
            "        DataPointAttributes['group'] AS ConsumerGroup,\n" +
            "        DataPointAttributes['partition'] AS PartitionId,\n" +
            "        argMax(Value, Time) AS Lag\n" +
            "    FROM metrics_gauge\n" +
            "    WHERE MetricName = {lagMetric:String} AND Time >= {from:DateTime64(9)} AND Time < {to:DateTime64(9)}" + topicClause + "\n" +
            "    GROUP BY Topic, ConsumerGroup, PartitionId";

        var sql = topic is not null
            ? "SELECT Topic, ConsumerGroup, PartitionId, toInt64(round(Lag)) AS Lag\n" +
              $"FROM (\n    {latest}\n)\n" +
              "ORDER BY ConsumerGroup, toUInt64OrNull(PartitionId) ASC NULLS LAST, PartitionId\n" +
              "LIMIT {limit:UInt32}"
            : "SELECT Topic, toInt64(round(sum(Lag))) AS Lag\n" +
              $"FROM (\n    {latest}\n)\n" +
              "GROUP BY Topic";

        return new MessagingSql(sql, parameters);
    }

    /// <summary>
    /// The per-span projection every spans query here aggregates over, filtered to classified
    /// spans: the inner level classifies each span (<c>SpanRole</c>) and derives the attribute
    /// columns; the middle level collapses it to <c>Role</c> (publish/consume) and flags, per
    /// consumer, whether it emitted any process spans (<c>HasProcess</c>, a window over
    /// system/destination/service/group); the trailing <c>WHERE</c> - which lands on the
    /// caller's own query level, so callers can append <c>AND ...</c> - drops receive spans of
    /// consumers that have process spans. See the class remarks.
    /// </summary>
    private static string SpanSource(string where) =>
        "(\n" +
        "    SELECT\n" +
        "        *,\n" +
        $"        if(SpanRole = '{PublishRole}', '{PublishRole}', '{ConsumeRole}') AS Role,\n" +
        "        max(SpanRole = 'process') OVER (PARTITION BY MsgSystem, MsgDestination, ServiceName, MsgGroup) AS HasProcess\n" +
        "    FROM (\n" +
        "        SELECT\n" +
        "            ServiceName,\n" +
        "            DurationNano,\n" +
        "            StatusCode = 'STATUS_CODE_ERROR' AS IsError,\n" +
        $"            {SystemExpr} AS MsgSystem,\n" +
        $"            {DestinationExpr} AS MsgDestination,\n" +
        $"            {PartitionExpr} AS MsgPartition,\n" +
        $"            {ConsumerGroupExpr} AS MsgGroup,\n" +
        "            toUInt64OrNull(SpanAttributes['messaging.message.body.size']) AS BodySize,\n" +
        $"            {SpanRoleExpr} AS SpanRole\n" +
        "        FROM spans\n" +
        $"        WHERE {where}\n" +
        "    )\n" +
        "    WHERE SpanRole != ''\n" +
        ")\n" +
        "WHERE (SpanRole != 'receive' OR NOT HasProcess)";

    private static string SpanWhere(ClickHouseParameterCollection parameters, int windowMinutes, DateTimeOffset end, string? service, string? system, string? destination)
    {
        var from = end.AddMinutes(-windowMinutes);
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
            "mapContains(SpanAttributes, 'messaging.system')",
        };

        if (!string.IsNullOrWhiteSpace(service))
        {
            parameters.AddParameter("service", service);
            clauses.Add("ServiceName = {service:String}");
        }

        if (!string.IsNullOrWhiteSpace(system))
        {
            parameters.AddParameter("system", system);
            clauses.Add($"{SystemExpr} = {{system:String}}");
        }

        if (destination is not null)
        {
            parameters.AddParameter("destination", destination);
            clauses.Add($"{DestinationExpr} = {{destination:String}}");
        }

        return string.Join(" AND ", clauses);
    }

    private static ClickHouseParameterCollection TimeParameters(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);
        return parameters;
    }
}
