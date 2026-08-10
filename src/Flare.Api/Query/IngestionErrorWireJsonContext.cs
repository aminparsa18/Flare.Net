using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Plain-PascalCase (no naming policy) contract for parsing <see cref="IngestionErrorEntryDto"/>
/// back out of <see cref="IngestionStatsKeys.ErrorsListKey"/> - matches the property names
/// <c>Flare.Ingest.Stats.IngestionErrorEntryJsonContext</c> actually wrote (its own doc
/// remarks explain the plain-name-for-redis-cli-debugging rationale). Deliberately a
/// separate context from <see cref="Json.IngestionJsonContext"/> (the camelCase one this
/// same record type also serializes through for the actual API response) rather than one
/// context serving both - System.Text.Json contexts are independent per set of options, so
/// reusing the record type across both is simpler than inventing a second DTO shape.
/// </summary>
[JsonSerializable(typeof(IngestionErrorEntryDto))]
public sealed partial class IngestionErrorWireJsonContext : JsonSerializerContext;
