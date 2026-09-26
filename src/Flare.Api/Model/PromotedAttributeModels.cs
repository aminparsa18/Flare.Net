using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One attribute key promoted to its own <c>MATERIALIZED</c> column on <c>logs</c> (ADR-0062).
/// <see cref="Backfilling"/> is true while a <c>MATERIALIZE COLUMN</c>/<c>MATERIALIZE INDEX</c>
/// mutation for this column is still running - filters on the key are already correct then
/// (ClickHouse computes the column from its expression for parts that don't store it yet),
/// just not yet faster on those older parts. No <see cref="DateTimeOffset"/> member, so this
/// carries <c>[GenerateTypeScript]</c>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PromotedAttributeInfo(
    AttributeBag Bag,
    string Key,
    string ColumnName,
    string IndexName,
    bool Backfilling);

/// <summary>
/// <c>GET /api/indexing/promoted-attributes</c> response. Hand-written on the MemoryPack TS
/// side - the <c>IReadOnlyList</c> member blocks <c>[GenerateTypeScript]</c>, same precedent
/// as <see cref="ApdexThresholdsResponse"/>.
/// </summary>
[MemoryPackable]
public sealed partial record PromotedAttributesResponse(
    IReadOnlyList<PromotedAttributeInfo> Attributes,
    int MaxPromotedAttributes);

/// <summary>
/// <c>POST /api/indexing/promoted-attributes</c> body. <see cref="Backfill"/> false skips the
/// <c>MATERIALIZE COLUMN</c>/<c>MATERIALIZE INDEX</c> mutations - only newly written parts
/// (and parts merged afterwards) get the physical column and skip index; worth it on a large
/// table whose older data ages out by TTL soon anyway.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PromoteAttributeRequest(AttributeBag Bag, string Key, bool Backfill);
