using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/messaging/destinations</c> - the <c>/messaging</c> page's
/// topic/queue list. A relative window plus optional end, same shape as
/// <see cref="HostListRequest"/>/<see cref="HostMetricsRequest"/>, and single-value
/// service/system filters rather than lists, so this carries <c>[GenerateTypeScript]</c>
/// (see <c>Flare.Api.csproj</c>'s MemoryPack TypeScript codegen comment). See
/// docs-internal/adr/0056-messaging-queue-monitoring.md.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MessagingDestinationsRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.MessagingQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now. Same convention as <see cref="HostMetricsRequest.EndUnixMs"/>.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c> of the producing/consuming span. Null/empty = all services.</summary>
    public string? Service { get; init; }

    /// <summary>Exact <c>messaging.system</c> (e.g. <c>kafka</c>, <c>rabbitmq</c>, <c>servicebus</c>). Null/empty = all systems.</summary>
    public string? System { get; init; }
}

/// <summary>
/// One <c>(messaging.system, messaging.destination.name)</c> pair's publish and consume
/// figures for the window - one row of the <c>/messaging</c> page's table. "Publish" and
/// "consume" are classified per span by <see cref="Query.MessagingQueryBuilder"/> (operation
/// attribute first, span kind as the fallback - see its remarks).
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MessagingDestination
{
    /// <summary><c>messaging.system</c>, e.g. <c>kafka</c>.</summary>
    public required string System { get; init; }

    /// <summary><c>messaging.destination.name</c> - the topic, queue or exchange. Empty when the instrumentation didn't set it (e.g. an anonymous destination).</summary>
    public required string Destination { get; init; }

    public required ulong PublishCount { get; init; }

    public required ulong PublishErrorCount { get; init; }

    /// <summary><see cref="PublishCount"/> divided by the window length in seconds.</summary>
    public required double PublishPerSecond { get; init; }

    /// <summary>Median publish-span duration, milliseconds. 0 when <see cref="PublishCount"/> is 0.</summary>
    public required double PublishP50Ms { get; init; }

    public required double PublishP99Ms { get; init; }

    public required ulong ConsumeCount { get; init; }

    public required ulong ConsumeErrorCount { get; init; }

    public required double ConsumePerSecond { get; init; }

    /// <summary>Median consume-span duration, milliseconds - handling time for a <c>process</c> span, fetch time for a <c>receive</c> span (ADR-0056). 0 when <see cref="ConsumeCount"/> is 0.</summary>
    public required double ConsumeP50Ms { get; init; }

    public required double ConsumeP99Ms { get; init; }

    /// <summary>Distinct services that published to this destination.</summary>
    public required ulong ProducerServiceCount { get; init; }

    /// <summary>Distinct services that consumed from this destination.</summary>
    public required ulong ConsumerServiceCount { get; init; }

    /// <summary>Mean <c>messaging.message.body.size</c> over every classified span that set it. Null when none did.</summary>
    public required double? AvgMessageBytes { get; init; }

    /// <summary>
    /// Kafka only: the sum over every consumer group and partition of the latest
    /// <c>kafka.consumer_group.lag</c> gauge value in the window (the OTel Collector's
    /// <c>kafkametrics</c> receiver). Null when that metric isn't being collected for this
    /// topic - not the same as 0.
    /// </summary>
    public required long? ConsumerLag { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/messaging/destinations</c>. Hand-written on the MemoryPack
/// TS side - the <c>IReadOnlyList&lt;T&gt;</c> member blocks <c>[GenerateTypeScript]</c>, same
/// precedent as <see cref="ServiceOverviewResponse"/>.
/// </summary>
[MemoryPackable]
public sealed partial record MessagingDestinationsResponse
{
    /// <summary>The window actually used, post-clamp.</summary>
    public required int WindowMinutes { get; init; }

    /// <summary>Busiest first (publish + consume count), capped at <see cref="Query.MessagingQueryBuilder.MaxDestinations"/>.</summary>
    public required IReadOnlyList<MessagingDestination> Destinations { get; init; }

    /// <summary>Every <c>messaging.system</c> seen in the window, sorted - the toolbar's system picker. Unaffected by <see cref="MessagingDestinationsRequest.System"/>, so narrowing doesn't shrink the picker.</summary>
    public required IReadOnlyList<string> Systems { get; init; }

    /// <summary>Every service that published or consumed in the window, sorted - the toolbar's service picker. Unaffected by <see cref="MessagingDestinationsRequest.Service"/>.</summary>
    public required IReadOnlyList<string> Services { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/messaging/destination-detail</c> - one destination's
/// producers, consumers, partitions and consumer lag. Same window/service fields as
/// <see cref="MessagingDestinationsRequest"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MessagingDestinationDetailRequest
{
    public required string System { get; init; }

    public required string Destination { get; init; }

    public int? WindowMinutes { get; init; }

    public long? EndUnixMs { get; init; }

    public string? Service { get; init; }
}

/// <summary>
/// One service's traffic on one destination, from one side: a producer row (publish spans,
/// <see cref="ConsumerGroup"/> always empty) or a consumer row (consume spans, one row per
/// service and consumer group).
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MessagingServiceStats
{
    public required string ServiceName { get; init; }

    /// <summary><c>messaging.consumer.group.name</c>, else the older <c>messaging.kafka.consumer.group</c>. Empty for producers and for systems without consumer groups.</summary>
    public required string ConsumerGroup { get; init; }

    public required ulong Count { get; init; }

    public required ulong ErrorCount { get; init; }

    public required double PerSecond { get; init; }

    public required double P50Ms { get; init; }

    public required double P99Ms { get; init; }

    public required double? AvgMessageBytes { get; init; }
}

/// <summary>One partition's publish/consume traffic on one destination. Kafka (and other partitioned systems) only.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MessagingPartitionStats
{
    /// <summary><c>messaging.destination.partition.id</c>, else the older <c>messaging.kafka.destination.partition</c>.</summary>
    public required string Partition { get; init; }

    public required ulong PublishCount { get; init; }

    public required double PublishPerSecond { get; init; }

    public required ulong ConsumeCount { get; init; }

    public required double ConsumePerSecond { get; init; }

    public required ulong ErrorCount { get; init; }
}

/// <summary>The latest <c>kafka.consumer_group.lag</c> value in the window for one consumer group on one partition.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MessagingConsumerLag
{
    public required string ConsumerGroup { get; init; }

    public required string Partition { get; init; }

    public required long Lag { get; init; }
}

/// <summary>Response body for <c>POST /api/messaging/destination-detail</c>. Hand-written on the MemoryPack TS side, same reason as <see cref="MessagingDestinationsResponse"/>.</summary>
[MemoryPackable]
public sealed partial record MessagingDestinationDetailResponse
{
    /// <summary>Echoed back from the request, so the drill-down titles itself off the response.</summary>
    public required string System { get; init; }

    public required string Destination { get; init; }

    public required int WindowMinutes { get; init; }

    /// <summary>Busiest first.</summary>
    public required IReadOnlyList<MessagingServiceStats> Producers { get; init; }

    /// <summary>Busiest first.</summary>
    public required IReadOnlyList<MessagingServiceStats> Consumers { get; init; }

    /// <summary>Numeric partition order where the ids are numeric. Empty when no span set a partition.</summary>
    public required IReadOnlyList<MessagingPartitionStats> Partitions { get; init; }

    /// <summary>Empty when <c>kafka.consumer_group.lag</c> isn't collected for this topic (or the destination isn't Kafka).</summary>
    public required IReadOnlyList<MessagingConsumerLag> ConsumerLag { get; init; }
}
