namespace Flare.Api.Query;

/// <summary>
/// Read-side mirror of <c>Flare.Ingest.Stats.IngestionStatsKeys</c> - same key/field format,
/// duplicated rather than shared (see <see cref="Model.IngestionSignal"/>'s remarks on why
/// there's no project reference between Flare.Api and Flare.Ingest). Kept deliberately
/// minimal: only the pieces <see cref="IngestionStatsQueryService"/> actually needs to read
/// back what the write side produces.
/// </summary>
public static class IngestionStatsKeys
{
    private const string MinuteBucketPrefix = "flare:ingestion:minute:";

    public const string ErrorsListKey = "flare:ingestion:errors";

    public const int MaxErrorEntries = 200;

    /// <summary>Builds the same key format as the write side, from a raw epoch-minute number.</summary>
    public static string MinuteBucketKey(long epochMinute) => MinuteBucketPrefix + epochMinute;

    public static string FieldPrefix(Model.IngestionSignal signal, Model.IngestionProtocol protocol) =>
        $"{signal.ToString().ToLowerInvariant()}:{protocol.ToString().ToLowerInvariant()}";
}
