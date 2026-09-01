# ADR-0016: MemoryPack TypeScript adoption for the dashboard, hand-written where the generator can't reach

Status: Accepted

Date: 2026-09-01

## Context

ADR-0015 made MemoryPack an opt-in, content-negotiated wire format on
`Flare.Api` - additive, JSON unconditionally unchanged - and deliberately
left "does the dashboard ever adopt it" as a separate, not-yet-made
decision (that ADR's Consequences, and the investigation doc's
follow-ups). This ADR is that decision, covering the `src/dashboard`
side: wiring `src/dashboard/src/lib/*-api.ts` (16 files: 15
`*-api.ts` files + `api.ts`) to send
`Accept`/`Content-Type: application/x-memorypack` and decode responses
through [MemoryPack's TypeScript
support](https://github.com/Cysharp/MemoryPack#typescript-and-aspnet-core-formatter)
instead of hand-mirrored `interface`s + `res.json()`.

Three things turned out to be true only after reading MemoryPack 1.21.4's
actual TypeScript generator source (not assumed from its README), and
all three reshape what "adopt MemoryPack in the dashboard" can mean
today:

- **The generator has no mapping for `DateTimeOffset`.** Its member-type
  resolution (`MemoryPack.Generator/TypeScriptMember.cs`) only handles
  `bool`/numeric primitives/`bigint`/`string`/`Guid`/enum/`DateTime` -
  anything else, `DateTimeOffset` included, throws
  `NotSupportedTypeException` (`MEMPACK031`, a compiler error) the moment
  `[GenerateTypeScript]` is attached. `Flare.Api`'s DTOs use
  `DateTimeOffset` for essentially every timestamp field - 18 of 28
  `Model/*.cs` files, including `LogEventDto`. Confirmed identical
  between MemoryPack's `main` branch and the exact `1.21.4` tag already
  pinned here, so this isn't a version-lag issue to wait out.
- **`DateTimeOffset`'s actual wire bytes are not something to
  reverse-engineer from private struct layout.** MemoryPack's *binary*
  path already serializes `DateTimeOffset` natively via
  `UnmanagedFormatter<DateTimeOffset>` - a raw copy of the CLR struct's
  private fields (an internal `DateTime` tick+Kind value plus a private
  `short` offset-minutes field) - which is not a MemoryPack-documented
  format. Rather than guess at that layout, it was captured empirically:
  a throwaway `dotnet run` called `MemoryPackSerializer.Serialize` on
  known `DateTimeOffset` values (UTC and a +5h offset, null and
  non-null `DateTimeOffset?`, nested inside a real `[MemoryPackable]`
  record) and the exact bytes were read back. Verified shape: 16 bytes -
  `offsetMinutes` as a plain `int64`, then the wall-clock tick value with
  its top 2 (Kind) bits masked off, i.e. exactly the same
  `dateTimeMask`/`unixEpochTicks` trick MemoryPack's own generated
  `writeDate`/`readDate` already use for `DateTime` - MemoryPack simply
  hasn't extended that trick to `DateTimeOffset` yet. A nullable
  `DateTimeOffset?` prepends an 8-byte `int64` has-value flag, 24 bytes
  total - the same pattern the generator's own `writeNullableInt64`/
  `writeNullableDate` use for every other unmanaged type.
- **The generator also has no mapping for `IReadOnlyList<T>`/
  `IReadOnlyDictionary<K,V>`, regardless of `T`.** Its collection-kind
  detection (`TypeMeta.ParseCollectionKind`) walks a type's
  `AllInterfaces` looking for the *mutable* `ICollection<T>`/`ISet<T>`/
  `IDictionary<K,V>` - which the read-only interface family doesn't
  extend (`IReadOnlyList<T>` implements `IReadOnlyCollection<T>`, a
  separate hierarchy). A member declared `IReadOnlyList<string>` fails
  `MEMPACK031` exactly like a `DateTimeOffset` member does - confirmed
  by attaching `[GenerateTypeScript]` to `ResourceNodeDto` (its `Urls:
  IReadOnlyList<string>` member) and to four otherwise-clean response
  wrappers (`ClusterStatusResponse`, `MetricNamesResponse`,
  `MetricAttributeKeysResponse`, `PipelineServiceBreakdown`) and hitting
  the same error on each. This wasn't caught by the 7 files landed in
  this ADR's first pass purely because none of their DTOs happen to have
  a collection-of-objects member - `Flare.Api/Model` uses
  `IReadOnlyList<T>`/`IReadOnlyDictionary<K,V>` as its standing
  convention for every list/map-shaped property, so this affects nearly
  every "list of X" response wrapper across all 16 files, independent of
  `DateTimeOffset`.
- **MemoryPack's enums are numeric ordinals on the wire, never the member
  name.** The JSON path used `UseStringEnumConverter`, so
  `user.role === 'Admin'` string comparisons are already scattered
  through ~20 dashboard files outside this migration's 16-file scope
  (`nav-links.ts`, `+layout.svelte`, ...). Switching those fields to
  MemoryPack's raw numeric `const enum` would force auditing/changing
  every one of those files too - a real scope expansion this decision
  did not sign up for.

## Decision

**All 16 client files are in scope, migrated where MemoryPack's
generator can reach and hand-written where it can't - not narrowed to a
subset.** Per Model type:

- A type with no `DateTimeOffset`/`JsonElement`/`IReadOnlyList<T>`/
  `IReadOnlyDictionary<K,V>` member anywhere in its nested closure gets
  `[GenerateTypeScript]` next to its existing `[MemoryPackable]` and is
  served by MemoryPack's real generated TypeScript class
  (`src/dashboard/src/lib/generated/memorypack/*.ts`). A "closure"
  matters here: a type is only generator-eligible if every type it
  nests is *also* generator-eligible, since the generator resolves a
  nested `[MemoryPackable]` member by importing
  `{Type}.serializeCore`/`deserializeCore` from a same-directory
  `{Type}.ts` it assumes was also generated.
- A type that fails that test (directly or via nesting) is hand-written
  instead - a plain TypeScript class under `src/dashboard/src/lib/memorypack/`
  that reproduces the *exact* `writeObjectHeader`/`tryReadObjectHeader` +
  per-member version-tolerant shape MemoryPack's own generator emits
  (verified against real generated output, e.g. `AuthUserDto.ts`), so it
  is byte-compatible with what `Flare.Api` actually sends/expects.
  `DateTimeOffset` members go through `$lib/memorypack/date-time-offset.ts`
  (the empirically-verified codec above); the one `JsonElement` case
  (`SavedView.State`/`SavedViewRequest.State`) reuses the existing
  "opaque raw JSON text" contract via a plain
  `writer.writeString`/`reader.readString()` call, mirroring
  `JsonElementMemoryPackFormatter.cs`'s own C# side exactly.
- **Every enum-bearing field converts at the client-module boundary**
  (`$lib/memorypack/enums.ts`'s `userRoleToString`/`userRoleFromString`,
  one per enum as more are migrated) back to the exact string-literal
  union the dashboard's ~20 other consumers already compare against.
  Every migrated function still returns/accepts this file's original
  hand-written public `interface` unchanged - decode → map → return the
  same shape, every time - so nothing outside `src/dashboard/src/lib/`
  needs to change for this phase.
- **Always-on once a file is migrated, no transition flag.** `Flare.Api`'s
  MemoryPack support is already fully additive and backward-compatible
  (ADR-0015) - there is no partial-rollout risk a flag would hedge
  against, and a flag nobody ever flips is dead code to remove later.
- **Codegen wiring: a `predev`/`prebuild`/`precheck` npm script**
  (`npm run codegen` → `dotnet build ../Flare.Api/Flare.Api.csproj`),
  not a Vite plugin. MemoryPack's TypeScript generator is a Roslyn
  incremental source generator that writes `.ts` files to disk as a
  side effect of compiling `Flare.Api` (skipped during IDE design-time
  builds) - there is nothing for a Vite plugin to hook into on the
  TypeScript-build side; the actual generation step is `dotnet build`,
  full stop. A plain npm pre-script is the simplest way to guarantee it
  runs before Vite/`svelte-check` ever look at `$lib/generated/`.
  `src/dashboard/src/lib/generated/memorypack/` is gitignored (the
  codegen step always regenerates it) - only the hand-written
  `$lib/memorypack/*.ts` and `$lib/*-api.ts` files are committed.

**All 16 of 16 files completed.** Landed in two passes: `auth-api.ts`,
`auth-settings-api.ts`, `entra-settings-api.ts`, `ldap-settings-api.ts`,
`oidc-settings-api.ts`, `proxy-auth-settings-api.ts` (all fully
generated - none of their DTOs carry a blocking member) plus
`users-api.ts` (the first fully hand-written file, chosen specifically
because `UserSummaryDto.CreatedAt` exercises the hand-rolled
`DateTimeOffset` codec against a real server) proved both paths -
generated and hand-written - end-to-end, live, before the
`IReadOnlyList<T>` finding above was even hit (none of those 7 files'
DTOs have a collection-of-objects member). The remaining 9 files
(`alerts-api.ts`, `indexing-api.ts`, `ingest-keys-api.ts`,
`ingestion-api.ts`, `metrics-api.ts`, `pipeline-api.ts`,
`saved-views-api.ts`, `traces-api.ts`, and the Logs/Resources/HostStats
functions in `api.ts`) followed the same mechanical pattern, extended by
the `IReadOnlyList<T>` rule discovered while landing them - hand-write
the `DateTimeOffset`/`JsonElement`/`IReadOnlyList<T>`-bearing types,
generate the rest, convert enums at the boundary. The full, corrected
per-type generated-vs-hand-written classification for all 16 files is
recorded in `../investigations/memorypack-serialization-migration-scope.md`'s
Phase 2 section. Final split: ~34 real generated classes, ~50
hand-written ones (a field-count consistency check - constructor
assignment count vs. every `writeObjectHeader`/`count ==`/`count >`
call site - was run across every hand-written file to catch
transcription mistakes before the live pass; it caught one real
off-by-one in `LogEventDto.ts`, fixed before verification).

## Alternatives considered

- **Convert every `DateTimeOffset` property to `DateTime` across
  `Flare.Api/Model`** to make every DTO generator-eligible in one pass.
  Rejected: a real, cross-cutting change to ~18 model files (plus
  whatever reads them) that loses `DateTimeOffset`'s explicit-UTC-offset
  semantics, for a payoff (skip hand-writing ~30-odd classes) that
  doesn't justify touching that much of `Flare.Api` in a change framed
  as "dashboard adoption." The JSON wire shape wouldn't even change
  (`DateTime` still serializes as the same ISO-8601 text), so this
  would be pure internal churn for MemoryPack's benefit alone.
- **A custom `[MemoryPackAllowSerialize]` `DateTimeOffset` formatter
  server-side** (writing it as a plain `long` UTC-ticks value), mirrored
  by the same shape in hand-written TypeScript - considered first,
  before checking whether it was actually necessary. Rejected once
  checked: MemoryPack's TypeScript generator resolves a member's TS type
  from its declared CLR type only and has no concept of a per-member
  custom formatter, so a `DateTimeOffset` member still can't carry
  `[GenerateTypeScript]` even with a custom formatter registered - this
  would have added a server-side wire-format change for zero additional
  types actually unlocked, solving a problem (unsafe wire format) that
  the empirical-verification approach solved without touching
  `Flare.Api` at all.
- **One vertical slice first** (e.g. just the auth-adjacent files),
  matching Phase 1's original fork question. Superseded by events: the
  DateTimeOffset/enum findings above forced a per-*type* (not per-file)
  generated-vs-hand-written split regardless of which files were
  targeted first, so "vertical slice" stopped being the operative
  question - the real one became "which types can the generator reach,"
  answered once for all 16 files' worth of DTOs rather than
  re-discovered file by file.
- **Rushing all 16 files' hand-written classes into the very first pass,
  before either proven path was live-verified.** Rejected for that first
  pass specifically: hand-written classes have no compiler/generator
  catching drift or transcription mistakes the way real codegen does -
  the stated bar for "done" here is build+tests green *and* a live check
  against a running server. Landing 7 files first (both a generated file
  and a hand-written one, live-verified) established the pattern and the
  verification method - including the field-count consistency check that
  later caught a real mistake - before scaling it to the remaining 9 in
  a second pass within the same ADR, rather than either skipping
  verification or discovering the `IReadOnlyList<T>` gap mid-way through
  an unreviewable 16-file diff.

## Consequences

- New MSBuild wiring in `src/Flare.Api/Flare.Api.csproj`:
  `MemoryPackGenerator_TypeScriptOutputDirectory` points at
  `src/dashboard/src/lib/generated/memorypack` (a relative path reaching
  across project boundaries - `Flare.Api`'s build now writes into
  `src/dashboard`'s source tree), `TypeScriptImportExtension = .js`
  (matches SvelteKit/Vite's bundler module resolution),
  `TypeScriptConvertPropertyName = true` (camelCase, matching the
  dashboard's existing hand-mirrored convention),
  `TypeScriptEnableNullableTypes = false` - deliberately *not* `true`
  despite MemoryPack's own README example: with it `true`, the generator
  emits a non-nullable `string` *field* type for a required C# `string`
  but still calls `reader.readString()` to populate it, a method whose
  own return type is unconditionally `string | null` (MemoryPack's
  string wire format always has a null representation) - a real
  `svelte-check` type error on every required-string member, found by
  actually turning it on rather than trusting the README's example
  verbatim. `false` keeps every generated string field `string | null`
  uniformly, matching what `readString()` can actually return; every
  migrated file's decode step falls the now-`string | null` field back
  to `?? ''` where the public interface promises a non-nullable
  `string` (safe: the field is never actually null on the wire, since
  it mirrors a `required string` in C#).
- `src/dashboard/package.json` gained a `codegen` script plus
  `predev`/`prebuild`/`precheck` hooks that run it - `npm run dev`/
  `build`/`check` all now shell out to `dotnet build` first. Anyone
  running the dashboard standalone needs the .NET SDK available, not
  just Node - a new local dev dependency this repo didn't have before
  for `src/dashboard` specifically (every other Flare project already
  needed it).
- `src/dashboard/.gitignore` gained `/src/lib/generated/memorypack` -
  regenerated on every build, never committed, same reasoning JSON DTOs
  were never duplicated into a second hand-maintained copy.
- New hand-written, committed files under `src/dashboard/src/lib/memorypack/`
  (~50 DTO classes across the 16 files, plus shared infrastructure):
  `date-time-offset.ts` (the verified `DateTimeOffset` codec), `enums.ts`
  (ordinal↔string-literal adapters - one exported pair per migrated enum,
  including the ones with no generated referrer at all, e.g.
  `AttributeBag`/`SavedViewPageType`/`SpanAttributeBag`), and `LogFilter.ts`
  (the one type reused across the most files - `alerts-api.ts`,
  `metrics-api.ts`'s `MetricFilter`, and `api.ts`'s own Logs endpoints -
  including its `logFilterToPlain`/`logFilterFromPlain` conversion
  helpers against `api.ts`'s existing plain interface). Every hand-written
  file documents in its own header comment exactly why it can't be
  generated and what it must stay byte-compatible with - there is no
  compiler to catch drift if the matching `Flare.Api/Model` type changes
  shape without a matching update here.
- `Flare.Api/Model/*.cs` gained `[GenerateTypeScript]` on every type that
  passed the eligibility test above - `UserModels.cs`
  (`SetUserRoleRequest`/`SetUserDisabledRequest`), `AlertModels.cs`
  (`AlertThreshold`), `IndexingModels.cs` (6 types),
  `IngestionModels.cs`/`IngestApiKeyModels.cs`/`MetricModels.cs`/
  `PipelineModels.cs` (several each),
  `LogValueDistributionRequest.cs`/`ResourceGraphDto.cs` (the one
  eligible type each - `LogAttributeKeyInfo` and `ResourceEdgeDto`).
- Verified: `dotnet build Flare.slnx` and `dotnet test Flare.slnx` both
  clean (782 tests, unchanged from Phase 1 plus normal drift), `npm run
  check` clean across all 5055 files in `src/dashboard` (0 errors,
  confirming the enum wire-format change didn't silently break any of
  the ~20 consumers outside this migration's scope either). Live-verified
  twice against a real `docker compose up` stack (ClickHouse + Redis +
  `Flare.Api`, no mocks) - once for the first 7 files, once for the
  remaining 9 - covering every one of the 16 files' endpoints: MemoryPack
  request+response round trips including real (non-default,
  non-empty) `DateTimeOffset` values decoded correctly four separate
  times (`users-api.ts`'s `CreatedAt`, `alerts-api.ts`'s
  `CreatedAt`/`UpdatedAt`, `ingest-keys-api.ts`'s `CreatedAt`,
  `saved-views-api.ts`'s `CreatedAt`/`UpdatedAt` - the last two created
  live via their own POST endpoints, not just read back), a byte-exact
  `JsonElement`-as-raw-JSON-text round trip for `SavedView.State`
  (a nested object, not just a flat value), and the Phase 1
  malformed-body-400 regression re-confirmed through the new client code
  paths. JSON baseline (every endpoint tested with no `Accept` override)
  reads byte-for-byte unchanged throughout.

## Related documentation

- [`../investigations/memorypack-serialization-migration-scope.md`](../investigations/memorypack-serialization-migration-scope.md) -
  Finding 4 (the original dashboard-scope flag), and this ADR's own
  "Phase 2" section: the exact empirical `DateTimeOffset` byte capture,
  the full per-type generated-vs-hand-written classification for all 16
  files, and the live e2e run this ADR's Consequences summarize.
- [`0015-memorypack-content-negotiation-for-flare-api.md`](0015-memorypack-content-negotiation-for-flare-api.md) -
  the server-side decision this one builds on (additive, JSON stays the
  default, `ApiSerialization` as the one seam every endpoint uses).
