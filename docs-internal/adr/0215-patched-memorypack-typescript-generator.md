# ADR-0215: Build Flare.Api with a patched MemoryPack TypeScript generator

Status: Superseded by [ADR-0216](0216-revert-to-hand-written-memorypack-decoders.md)

Date: 2026-10-13

Supersedes the "hand-written where the generator can't reach" part of
[ADR-0016](0016-memorypack-dashboard-typescript-adoption.md); the rest of that ADR (content
negotiation, enum adapters, `EnableNullableTypes=false`) still stands.

## Context

ADR-0016 found that MemoryPack 1.21.4's TypeScript generator (`[GenerateTypeScript]`) throws
`MEMPACK031` for `DateTimeOffset`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` and
`IReadOnlyDictionary<K,V>` members, so 147 of Flare.Api's wire types had a hand-written
`src/dashboard/src/lib/memorypack/*.ts` mirror. `Flare.Api/Model` uses `DateTimeOffset` for
nearly every timestamp and `IReadOnlyList<T>` for every list, so the mirrors had to be kept in
sync with the C# models by hand, field by field.

## Decision

`Flare.Api` references `MemoryPack.Generator.DateTimeOffsetPatch` (a fork of the 1.21.4 generator,
github.com/aminparsa18/MemoryPack, published to nuget.org) instead of the generator inside the
`MemoryPack` meta package (`ExcludeAssets="analyzers"`; `MemoryPack.Core` stays official 1.21.4).
The fork adds to the TypeScript generator:

- `DateTimeOffset`/`DateTimeOffset?` (upstream PR Cysharp/MemoryPack#459),
- `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyDictionary<K,V>` (same wire format as
  `List<T>`/`Dictionary<K,V>`, so the existing array/`Map` emission applies),
- nullable readers that inspect only the `HasValue` byte: C# blits `Nullable<T>` with padding that
  is not guaranteed to be zero, and the stock `readNullableInt64` etc. treat a null with stale
  padding as a value (seen in 2 of 500 round-tripped payloads),
- rewriting `MemoryPackWriter.ts`/`MemoryPackReader.ts` when their content differs (stock only
  writes them when missing, so an old runtime outlives a generator upgrade).

141 of the 147 hand-written classes are now generated. Six remain hand-written
(`Dashboard`, `DashboardRequest`, `DashboardListResponse`, `SavedView`, `SavedViewRequest`,
`SavedViewListResponse`) because of `JsonElement`.

## Consequences

- Wire types are defined once, in C#. Generated string members are `string | null`, so the
  `-api.ts` boundary adds `?? ''` for required strings; generated dictionaries are `Map`s, so
  `#lib/memorypack/map-record.ts` converts to the `Record<string, string>` the UI uses (the old
  hand-written decoders built the `Record` directly, which was marginally cheaper).
  `#lib/log-filter-plain.ts` holds the `LogFilter` plain<->wire conversions that used to live in
  the hand-written class.
- Flare depends on an unofficial package under the maintainer's own nuget.org account. Exit: when
  Cysharp ships these changes, remove the `DateTimeOffsetPatch` reference and `ExcludeAssets` (TODO
  comments in `Directory.Packages.props` and `Flare.Api.csproj`). The patch must be re-based on each
  MemoryPack release Flare upgrades to.
- A C#-filled, TS-decoded, TS-re-encoded round trip of every generated type (500 payloads) was
  used to verify the wire compatibility; it is not part of CI.
