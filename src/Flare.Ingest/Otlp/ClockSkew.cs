namespace Flare.Ingest.Otlp;

/// <summary>
/// The nanosecond delta between when <c>Flare.Ingest</c> received a batch and one
/// record's own event time - the single formula/sign convention every OTLP receiver
/// (gRPC + HTTP, all three signals) uses when building the per-service skew figures
/// <see cref="Stats.ServiceBreakdown.Build"/> aggregates. Centralized rather than
/// duplicated at each of the six call sites (unlike the attribute-flattening helpers
/// <see cref="OtlpLogMapper"/>/<see cref="OtlpTraceMapper"/>/<see cref="OtlpMetricsMapper"/>
/// each legitimately re-implement) because a sign mistake here would silently corrupt
/// the "ahead of/behind the server" meaning the dashboard renders - see
/// ADR-0014 for the full rationale and sign convention.
/// </summary>
public static class ClockSkew
{
    /// <summary>
    /// Positive when <paramref name="eventTime"/> is in the past relative to
    /// <paramref name="ingestedAt"/> (the expected case: network + processing
    /// latency). Negative when <paramref name="eventTime"/> claims a time in the
    /// future relative to receipt - the client's clock is ahead of this server's.
    /// </summary>
    public static long Nanos(DateTimeOffset ingestedAt, DateTimeOffset eventTime) =>
        (ingestedAt - eventTime).Ticks * 100;
}
