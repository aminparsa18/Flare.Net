using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Read-side mirror of <c>Flare.Ingest.Stats.FlushHealthKeys</c> - same key/field format,
/// duplicated rather than shared (see <see cref="IngestionSignal"/>'s remarks).
/// </summary>
public static class FlushHealthKeys
{
    private const string Prefix = "flare:ingestion:flush:";

    public static string HashKey(IngestionSignal signal) => Prefix + signal.ToString().ToLowerInvariant();

    public const string LastFlushAtField = "lastFlushAt";
    public const string LastBatchSizeField = "lastBatchSize";
    public const string LastErrorAtField = "lastErrorAt";
    public const string LastErrorField = "lastError";
    public const string ConsecutiveErrorsField = "consecutiveErrors";
}
