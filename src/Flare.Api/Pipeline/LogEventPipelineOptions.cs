namespace Flare.Api.Pipeline;

/// <summary>
/// Read-side mirror of <c>Flare.Ingest.Pipeline.LogEventPipelineOptions</c>, bound from the
/// same <c>LogEventPipeline</c> configuration section - not project-referenced (see
/// <c>Flare.Api.Model.IngestionSignal</c>'s remarks on why there's no reference between
/// Flare.Api and Flare.Ingest), so this only mirrors the two fields Flare.Api actually reads
/// (<see cref="StreamKey"/>/<see cref="ConsumerGroup"/>), not the full pipeline-tuning
/// surface (<c>BatchSize</c>, <c>FlushInterval</c>, etc.) that only Flare.Ingest's flush
/// worker acts on.
///
/// Binding the *same section name* here, rather than hand-copying Flare.Ingest's default
/// values as literals (the bug this type fixes - see <c>Query.PipelineStreamKeys</c>'s
/// remarks), means a deployment that sets <c>LogEventPipeline__StreamKey</c> /
/// <c>LogEventPipeline__ConsumerGroup</c> as an env var now reaches Flare.Api too - as long
/// as the same override is applied to Flare.Api's own environment (the two processes still
/// share no config source; this makes the override *possible* to propagate, not automatic
/// across processes). Consumed by <c>Query.PipelineStreamKeys</c> and
/// <c>LiveTail.LogTailBroadcaster</c> (this signal only, for its own live-tail read).
/// </summary>
public sealed class LogEventPipelineOptions
{
    public const string SectionName = "LogEventPipeline";

    /// <summary>
    /// Redis Stream key that buffers pending log events. Must match Flare.Ingest's
    /// <c>LogEventPipelineOptions.StreamKey</c>.
    /// </summary>
    public string StreamKey { get; set; } = "flare:logs";

    /// <summary>
    /// Consumer group Flare.Ingest's <c>ClickHouseFlushWorker</c> reads via
    /// <c>XREADGROUP</c>/<c>XACK</c>. Must match Flare.Ingest's
    /// <c>LogEventPipelineOptions.ConsumerGroup</c>.
    /// </summary>
    public string ConsumerGroup { get; set; } = "flare-ingest";
}
