namespace Flare.Ingest.Stats;

/// <summary>
/// One entry in the <see cref="IngestionStatsKeys.ErrorsListKey"/> recent-errors list -
/// the wire format for both the write side (<see cref="RedisIngestionStatsTracker"/>) and
/// the read side (Flare.Api's ingestion-stats endpoint). <c>Signal</c>/<c>Protocol</c> are
/// plain strings (not the enums directly) so this stays readable via <c>redis-cli LRANGE</c>
/// without a converter, same rationale <see cref="IngestionStatsKeys.FieldPrefix"/> uses.
/// Deliberately carries no request body/payload snippet - a reason string is enough for a
/// v1 "what's failing" signal without risking logging sensitive structured-log content
/// from a rejected export.
/// </summary>
public sealed record IngestionErrorEntry(
    DateTimeOffset Timestamp,
    string Signal,
    string Protocol,
    string Reason);
