# ADR-0276: Generated MemoryPack TypeScript decoders with Record dictionaries

Status: Proposed

Date: 2026-11-04

Supersedes [ADR-0216](0216-revert-to-hand-written-memorypack-decoders.md); restores the
arrangement of [ADR-0215](0215-patched-memorypack-typescript-generator.md), with one generator option
added.

## Context

ADR-0216 reverted the generated decoders because decode plus the `api.ts` mapping was about 11% slower
for a 200-row `LogSearchResponse` page. The generator emitted every `IReadOnlyDictionary<string, string>`
as a `Map`, and every consumer then copied it into a `Record` with `mapToRecord`. The hand-written
`string-record.ts` decoded straight into a `Record`.

The fork (github.com/aminparsa18/MemoryPack) adds a `MemoryPackGenerator_TypeScriptDictionaryAsRecord`
build property, which defaults to false so upstream behaviour does not change. When it is true:

- string-to-string dictionaries are emitted as `Record<string, string> | null` and read with
  `readStringRecord`. A null key or value becomes `''`, the same as `string-record.ts` did.
- Other string-, Guid- or 8-to-32-bit-number-keyed dictionaries use `readRecord`/`writeRecord`.
- long, enum, Date and object keys stay `Map`.

The wire format does not change.

ADR-0216 also recorded a correctness gap in the hand-written decoders: the stock `readNullable*` helpers
read the whole `Nullable<T>` flag. As a result, a null whose padding bytes are non-zero (C# blits
`Nullable<T>` as-is) decodes as a value. The patched runtime reads only the `HasValue` byte.

## Measurements (2026-11-04)

The bench was `src/dashboard/bench`, run on Node 26.5. There were 12 rounds alternating main
(hand-written) and this branch (generated with the option on), with the order rotated each round. Both
sides used byte-identical fixtures: 949 B for one event and 185,289 B for the page, checked with
sha256. Paired is the median per-round ratio against main; IQR is its interquartile range.

| Case | main ops/s | generated ops/s | Paired | IQR |
|---|---|---|---|---|
| Decode, 1 event | 123,114 | 117,413 | -2.0% | [-7.1, 2.2] |
| Decode + map, 1 event | 97,724 | 98,745 | +0.2% | [-3.0, 3.8] |
| Decode, 200-row page | 576 | 572 | +0.1% | [-5.1, 1.6] |
| Decode + map, 200-row page | 470 | 474 | -1.1% | [-4.5, 1.8] |
| Encode, 200-row page | 950 | 932 | -1.1% | [-3.4, 2.6] |

Every IQR spans zero, so the difference is within noise.

Round trip: C# filled every one of the 250 `[GenerateTypeScript]` types by reflection, in four
variants: full, value-type nullables null, the same nulls with 0xAB padding, and all null. 680 dirty
`Nullable<T>` fields in total. Each was serialized with MemoryPackSerializer, then TS decoded and
re-encoded it. All 1,000 fixtures came back byte-identical, and every dirty fixture re-encoded to its
clean counterpart. The same harness run against main's hand-written decoders failed 649 of 1,000.

## Decision

Go back to generated decoders for every type the generator can map. Flare.Api references
`MemoryPack` with `ExcludeAssets="analyzers"` plus `MemoryPack.Generator.Patched` 1.21.4.3 (nuget.org;
published from aminparsa18/MemoryPack main @ 0b4375a), and sets
`MemoryPackGenerator_TypeScriptDictionaryAsRecord=true`. `MemoryPack.Generator.Patched` replaces
ADR-0215's `MemoryPack.Generator.DateTimeOffsetPatch` 1.21.4.2. The package was renamed because it now
carries more than the DateTimeOffset patch, and the old ID is deprecated in its favour. `map-record.ts` is deleted, and consumers use
`dto.attributes ?? {}` directly.

The six types that carry a `JsonElement` (`Dashboard*`, `SavedView*`) stay hand-written, along with
`date-time-offset.ts` (which they use) and `enums.ts` (ADR-0016 enum adapters).

## Consequences

- C# models are once again the single source of truth for wire types. The duplication that ADR-0216
  brought back goes away.
- The nullable-padding bug ADR-0216 called out is gone for every generated type.
- Flare depends on the fork's package until Cysharp ships equivalent support. An upstream PR for the
  read-only collections, the `HasValue`-only nullable read and the Record option is still worth
  opening.
