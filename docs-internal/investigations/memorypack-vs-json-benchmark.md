# Investigation: Flare-specific JSON-vs-MemoryPack benchmark

Date: 2026-09-05
Related: [SerializerBenchmark](https://github.com/aminparsa18/SerializerBenchmark)
(external repo - generic payload-shape numbers that originally motivated adopting
MemoryPack), ADR-0015 (MemoryPack content negotiation for Flare.Api), ADR-0016
(MemoryPack dashboard TypeScript adoption), ADR-0017 (MemoryPack for the Ingest Redis
buffer)

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

## Consequences / follow-ups

- No decision changes as a result of this investigation - ADR-0015/0016/0017 all stand.
  Finding 2 is new information worth knowing (the TypeScript-side benefit is payload
  size, not decode speed) but doesn't argue for reverting ADR-0016: the dashboard is
  bandwidth-bound more often than CPU-bound on a typical client device, and the
  generated decoders' type safety was also part of that decision.
- Not investigated here: whether MemoryPack's TypeScript decode cost would look
  different with a hand-optimized (rather than generated) decoder, or under a JS engine
  other than V8 (Node's default) - e.g. a WebKit/JavaScriptCore-based browser.
- `src/Flare.Benchmarks` and `src/dashboard/bench/` are left in the repo (not
  removed after this run) so the numbers above can be reproduced or re-checked after a
  future MemoryPack/`.NET` version bump - see each's own README for how to run them.
