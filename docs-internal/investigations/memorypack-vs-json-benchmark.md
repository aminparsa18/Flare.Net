# Investigation: Flare-specific JSON-vs-MemoryPack benchmark

Date: 2026-09-05
Related: [SerializerBenchmark](https://github.com/aminparsa18/SerializerBenchmark)
(external repo - generic payload-shape numbers that originally motivated adopting
MemoryPack), ADR-0015 (MemoryPack content negotiation for Flare.Api), ADR-0016
(MemoryPack dashboard TypeScript adoption), ADR-0017 (MemoryPack for the Ingest Redis
buffer), [Cysharp/MemoryPack#459](https://github.com/Cysharp/MemoryPack/pull/459)
(upstream PR adding `DateTimeOffset` TypeScript-generator support - not merged as of this
writing; its own wire-format re-verification is what surfaced Finding 5)

The question behind this investigation: both migrations above were decided using the
external SerializerBenchmark repo's generic payload shapes, not Flare's actual
`LogEvent`/`LogEventDto`-shaped traffic with representative attribute-bag sizes and
cardinality. This gets project-local numbers for a decision already made -
`docs-internal/planning/roadmap.md`'s "Flare-specific JSON-vs-MemoryPack benchmark" item
- not a re-litigation of that decision. See `src/Flare.Benchmarks/` (the .NET half) and
`src/dashboard/bench/` (the TypeScript half) for the actual benchmark code and fixtures.

## Method

**.NET**: a new BenchmarkDotNet project (`src/Flare.Benchmarks`) exercising both
migrated boundaries directly with real production types - `Pipeline.RedisEventPayload`'s
MemoryPack path vs the legacy `LogEventJsonContext` JSON path it replaced (ADR-0017), and
`Flare.Api`'s `Json.ApiSerialization` MemoryPack path vs its `LogsJsonContext` JSON
default (ADR-0015) - calling `MemoryPackSerializer`/`JsonSerializer` directly rather than
through those wrapper types, since HTTP content negotiation and the Redis buffer's
one-time upgrade tag byte aren't part of either codec's actual serialization cost. Two
attribute-bag sizes per boundary (`Typical` ≈ 6 resource + 5 log attributes, modeled on
common OTel semantic-convention keys; `AttributeHeavy` ≈ 24 + 20, longer values, higher
cardinality - neither lifted from a captured production payload, so treat absolute
numbers as directionally representative), plus a genuine batch shape per boundary (1,000
individual Redis-buffer encode/decode calls looped in one benchmark method - matching how
`ClickHouseFlushWorker`/the sinks actually call it, since there's no batched wire
envelope; a full 200-row `LogSearchResponse` page, matching
`Query.LogSearchQueryBuilder.DefaultPageSize` - a real array-in-one-payload shape, unlike
the Redis-buffer side).

**TypeScript**: a [tinybench](https://github.com/tinylibs/tinybench) script
(`src/dashboard/bench/memorypack-vs-json.bench.ts`) comparing MemoryPack's generated
TypeScript decoders (ADR-0016) against plain `JSON.parse`/hand-written interface parsing,
for the same `LogEventDto`/`LogSearchResponse` shapes (one row, and a 200-row page) - the
dashboard had no test/benchmark runner at all, so tinybench was chosen directly over
pulling in a full framework (e.g. Vitest) just for its `bench()` API, a decision made
when this benchmark was added.

## Finding 1: On .NET, MemoryPack wins on every axis, both boundaries

| Redis buffer (`RedisEventPayload` vs `LogEventJsonContext`) | MemoryPack | JSON | Speedup | Alloc ratio (JSON/MemoryPack) |
|---|---:|---:|---:|---:|
| Typical event, encode | 680 ns | 1,315 ns | 1.9x | 1.3x |
| Typical event, decode | 1,194 ns | 3,766 ns | 3.2x | 4.2x |
| AttributeHeavy event, encode | 1,948 ns | 2,837 ns | 1.5x | ~1.0x |
| AttributeHeavy event, decode | 3,892 ns | 7,586 ns | 1.9x | 11.0x |
| 1,000-event batch, encode | 835 µs | 1,508 µs | 1.8x | 1.3x |
| 1,000-event batch, decode | 1,361 µs | 3,891 µs | 2.9x | 4.1x |

| API response (`ApiSerialization` vs `LogsJsonContext`) | MemoryPack | JSON | Speedup | Alloc ratio (JSON/MemoryPack) |
|---|---:|---:|---:|---:|
| Single `LogEventDto`, encode | 657 ns | 1,282 ns | 1.9x | 1.35x |
| Single `LogEventDto`, decode | 1,122 ns | 3,514 ns | 3.1x | 4.2x |
| 200-row page, encode | 215 µs | 404 µs | 1.9x | 1.4x |
| 200-row page, decode | 329 µs | 890 µs | 2.7x | 1.1x |

Machine: Intel Core i5-1038NG7, macOS, .NET 10.0.0 RyuJIT, BenchmarkDotNet v0.15.8. Full
detailed-distribution output (histograms, outlier counts, confidence intervals) is in
`src/Flare.Benchmarks/BenchmarkDotNet.Artifacts/results/` when regenerated (gitignored,
not committed - see that project's README to reproduce). Decode is where the gap is
widest (2.7-3.2x on single rows), which is the direction that matters most in practice:
`ClickHouseFlushWorker` and the dashboard's query paths both decode far more than they
encode.

## Finding 2: On TypeScript, native JSON is actually *faster* than MemoryPack's generated decoders - the opposite of the .NET result

| `LogEventDto`/`LogSearchResponse` (MemoryPack generated decoder vs `JSON.parse`+hand-written parse) | MemoryPack (median) | JSON (median) | JSON is faster by |
|---|---:|---:|---:|
| Single row, encode | 5.58 µs | 3.85 µs | 1.45x |
| Single row, decode | 8.45 µs | 4.47 µs | 1.89x |
| 200-row page, encode | 1.11 ms | 0.72 ms | 1.55x |
| 200-row page, decode | 1.76 ms | 0.80 ms | 2.22x |

V8's native `JSON.parse`/`JSON.stringify` (highly optimized C++, no JS-level function
call per field) beats MemoryPack's generated TypeScript decoder (`DataView` calls,
`BigInt` arithmetic for every `DateTimeOffset` field, `Map` construction for every
attribute dictionary - see `$lib/generated/memorypack/MemoryPackWriter.ts`/`MemoryPackReader.ts`,
which is a reasonably efficient hand-rolled implementation, not a strawman) at both
scales, for both directions. This is the reverse of Finding 1 - MemoryPack's win on the
.NET side comes from source-generated JSON still going through `Utf8JsonWriter`/
`Utf8JsonReader`'s general-purpose machinery, whereas in the browser/Node runtime,
native JSON *is* the fast path and a hand-written binary codec has nothing analogous to
beat.

MemoryPack does still produce a meaningfully smaller wire payload - 945 B vs 1,151 B for
one row, 184,489 B vs 225,975 B for a 200-row page (~18% smaller both times) - so the
actual benefit of ADR-0016 on the TypeScript side is payload size (and the generated
decoders' compile-time type safety against the C# DTOs), not client-side parse speed.
That's a materially different justification than "faster," and worth knowing before
citing this migration as a client-side performance win.

## Finding 3: `DateTimeOffset` genuinely isn't supported by MemoryPack 1.21.4's TypeScript generator - confirmed empirically, not just from the existing code comment

`$lib/memorypack/date-time-offset.ts`'s header comment claims any `[GenerateTypeScript]`
DTO with a `DateTimeOffset` member throws `MEMPACK031`, citing MemoryPack's public docs'
"unsupported types" list only names `char`/`decimal`, not `DateTimeOffset` - worth
independently confirming rather than trusting either source. Two checks:

1. **Decompiled `MemoryPack.Generator.dll` 1.21.4** (`ilspycmd`). The TypeScript
   generator's `TypeScriptMember.ConvertFromSpecialType` switches purely on Roslyn's
   `SpecialType` enum - the compiler's small hardcoded set of "special" BCL types.
   `DateTime` is one (`SpecialType.System_DateTime` → `Date`/`writeDate`/`readDate`);
   **`DateTimeOffset` isn't a `SpecialType` at all** (only `DateTime` is), and unlike
   `Guid` (which gets an explicit `SymbolEqualityComparer` check), there's no manual
   fallback for it either - it falls through to `NotSupportedTypeException` →
   `MEMPACK031`. A `System_DateTimeOffset` symbol does exist in the generator, but only
   for an unrelated unmanaged-struct-layout check on the core binary format - nothing to
   do with TypeScript codegen.
2. **Reproduced with a real throwaway build**: `[MemoryPackable, GenerateTypeScript]`
   with a `DateTimeOffset` member → `error MEMPACK031: ... type
   'global::System.DateTimeOffset' is not supported type in typescript generation`.
   Swapping the member to plain `DateTime` in the same project built clean and generated
   a real `foo: Date` field with `writeDate`/`readDate` calls - confirming the gap is
   specific to `DateTimeOffset`, not a general generator failure.

Also checked nuget.org: 1.21.4 is still the latest `MemoryPack.Generator` release, so
there's no newer version where this might already be fixed. **Conclusion: the hand-written
`date-time-offset.ts` (and every DTO that calls it) remains genuinely necessary - no
roadmap item, no architecture change.**

## Finding 4: two follow-up TypeScript optimizations applied - decode improved ~8-9%, encode unchanged

Following Finding 2, two of its recommendations were implemented and re-measured (6 runs
before, 6 after, discounting one clear outlier run - system noise, ~3.5% error margin vs.
the usual ~1.3%):

1. **`Map` → plain `Record<string, string>` for every attribute dictionary**
   (`resourceAttributes`/`scopeAttributes`/`logAttributes`/`spanAttributes`/`attributes`
   across `LogEventDto`/`SpanDto`/`SpanEventDto`/`MetricSeries`), via a new shared
   `$lib/memorypack/string-record.ts` helper (`writeStringRecord`/`readStringRecord`)
   replacing `MemoryPackWriter.writeMap`/`MemoryPackReader.readMap`. This wasn't just a
   micro-optimization in the abstract: every consumer (`api.ts`/`traces-api.ts`/
   `metrics-api.ts`) was already converting the decoded `Map` into a `Record` one line
   later via a `key ?? ''`/`value ?? ''` loop (the public interfaces were `Record`-typed
   all along) - so this also deleted three duplicated `toRecord` helpers and an entire
   redundant conversion pass, not just changed the decoder's internal representation.
2. **Fast-path `offsetMinutes === 0n`** in `writeDateTimeOffset`/`readDateTimeOffset`
   (`date-time-offset.ts`) - skips the `* ticksPerMinute` `BigInt` multiply for the
   common case (every `DateTimeOffset` in `Flare.Api/Model` already originates as UTC).

| Scenario | Before (avg) | After (avg) | Change |
|---|---:|---:|---:|
| Single row, encode | 5.56 µs | 5.59 µs | -0.5% (no meaningful change) |
| Single row, decode | 9.08 µs | 8.38 µs | +7.7% faster |
| 200-row page, encode | 1.10 ms | 1.09 ms | +1.4% (no meaningful change) |
| 200-row page, decode | 1.88 ms | 1.70 ms | +9.5% faster |

**Decode improved meaningfully; encode did not.** The `Map`→`Record` change helps decode
because it replaces `Map` construction (a `new Map()` plus N `.set()` calls, separate
hash-table bookkeeping) with direct object-literal property assignment - genuinely
cheaper in V8. It doesn't help encode by the same margin because `writeStringRecord`
still has to call `Object.keys(value)` to iterate a plain object's keys (an extra array
allocation `Map.forEach` didn't need), which roughly offsets what avoiding `Map` iteration
saves. A further tweak (`for...in` instead of `Object.keys()`, avoiding that array
allocation) was identified but not applied - not one of the two recommendations asked
for, and not verified.

Correctness was verified three ways, not just by `svelte-check` (which only checks
types): a TS-to-TS round-trip (populated `LogEventDto`/`LogSearchResponse` → serialize →
deserialize → deep-equal, including the empty-dictionary and null-dictionary cases); and
cross-language interop - a real `MemoryPackSerializer.Serialize` call from a throwaway C#
program, decoded on the TypeScript side via the new `readStringRecord`, byte-for-byte
matching. The wire format is unchanged (payload sizes are identical to Finding 2's
numbers: 945 B / 184,489 B for MemoryPack) - only the in-memory TypeScript
representation differs.

This does **not** change Finding 2's overall conclusion: JSON is still faster on every
axis. The gap narrowed for decode (1.89x → ~1.75x for a single row; 2.22x → ~2.10x for a
200-row page) but stayed roughly the same for encode - a structural ceiling either
recommendation was already flagged as unable to close (per-field JS function-call
dispatch vs. V8's single native JSON pass).

## Finding 5: `date-time-offset.ts` had a real correctness bug for non-zero offsets - found via an upstream contribution, fixed and re-verified

Finding 3 confirmed `DateTimeOffset` genuinely needs a hand-written wrapper. A follow-up
attempt to upstream that wrapper into MemoryPack's TypeScript generator itself
([Cysharp/MemoryPack#459](https://github.com/Cysharp/MemoryPack/pull/459)) re-verified
the wire format from scratch (per its own instructions not to trust a copied layout
without measuring it) and found the actual layout differs from what `date-time-offset.ts`
had assumed:

| | Assumed (wrong) | Actual (verified) |
|---|---|---|
| `offsetMinutes` field | 8 bytes, signed 64-bit | **4 bytes, signed 32-bit** (+ 4 bytes padding) |
| Ticks field | "local" ticks (needs `± offsetMinutes * ticksPerMinute` to get UTC) | **UTC ticks directly** - the offset is metadata only, never used to compute the instant |

The wrong assumption's arithmetic happens to reduce to a no-op when `offsetMinutes` is
`0` - which is every value Flare's own data ever produces (ClickHouse timestamps,
`UtcNow`) - so it was never actually exercised end-to-end against a non-UTC value in
this repo. Re-verified independently on this machine (net10.0, MemoryPack 1.21.4, not
just trusted from the PR) with `MemoryPackSerializer.Serialize` against zero/positive/
negative offsets, `MinValue`/`MaxValue`, and sub-millisecond ticks - all matched the
corrected layout exactly. Reproduced the actual failure mode with the *old* code against
a real negative-offset value (`-5h`): it decoded 2026-09-05T17:00:00Z as
`-006140-07-21T17:44:00.000Z` - silently, no thrown error.

Fixed in `date-time-offset.ts` to match the verified layout. This also simplified the
code: since ticks are unconditionally UTC on the wire regardless of `offsetMinutes`, no
tick-adjustment math is needed at all anymore (not even the fast-pathed version from
Finding 4's recommendation #2) - `offsetMinutes` is now carried through as pure metadata.
Re-verified via TS-to-TS round-trip and cross-language interop (real C#-encoded bytes
for all the cases above, decoded correctly on the TypeScript side, including the
previously-broken negative-offset cases).

This bug was latent, not hypothetical - it would have silently produced wrong data the
moment any `DateTimeOffset` with a non-zero offset flowed through this path. Worth
remembering next time this file's "every value already originates as UTC" invariant is
touched: that invariant is what kept this dormant, not correct code.

## Consequences / follow-ups

- No decision changes as a result of this investigation - ADR-0015/0016/0017 all stand.
  Finding 2 is new information worth knowing (the TypeScript-side benefit is payload
  size, not decode speed) but doesn't argue for reverting ADR-0016: the dashboard is
  bandwidth-bound more often than CPU-bound on a typical client device, and the
  generated decoders' type safety was also part of that decision.
- Not investigated here: whether MemoryPack's TypeScript decode cost would look
  different with a hand-optimized (rather than generated) decoder, or under a JS engine
  other than V8 (Node's default) - e.g. a WebKit/JavaScriptCore-based browser.
- Not applied (Finding 4): the `for...in`-instead-of-`Object.keys()` encode tweak, or
  a lookup-table-based `Guid` hex encode/decode (the latter lives in the *generated*
  `MemoryPackWriter.ts`/`MemoryPackReader.ts` runtime, regenerated on every
  `dotnet build` - not something to hand-maintain the way `date-time-offset.ts`/
  `string-record.ts` are).
- `src/Flare.Benchmarks` and `src/dashboard/bench/` are left in the repo (not
  removed after this run) so the numbers above can be reproduced or re-checked after a
  future MemoryPack/`.NET` version bump - see each's own README for how to run them.
