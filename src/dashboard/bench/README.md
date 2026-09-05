# memorypack-vs-json.bench.ts

TypeScript half of `docs-internal/planning/roadmap.md`'s (now-removed) "Flare-specific
JSON-vs-MemoryPack benchmark" item — MemoryPack's generated decoders (ADR-0016) vs plain
`JSON.parse`/hand-written interface parsing, for the same `LogEventDto`/`LogSearchResponse`
response shapes the dashboard actually receives. See `src/Flare.Benchmarks/README.md` for
the .NET half this mirrors, and `docs-internal/investigations/` for the results this
benchmark actually produced.

**Uses [tinybench](https://github.com/tinylibs/tinybench) directly, not a test
framework** — the dashboard had no test/benchmark runner at all when this was added, and
a one-off micro-benchmark didn't justify pulling in Vitest just to get its `bench()` API.

## Running

```bash
npm run bench   # regenerates $lib/generated/memorypack/*.ts first, then runs the bench
```

Not wired into `svelte-check`/CI — it's a manual, one-off measurement, same as the .NET
benchmarks.

## Headline finding (see the investigation doc for full numbers)

Unlike the .NET side (where MemoryPack wins on every axis), in the browser/Node runtime
**V8's native `JSON.parse`/`JSON.stringify` are faster than MemoryPack's generated
TypeScript decoders** for encode *and* decode, at both the single-row and full-page
(200-row) scale — hand-rolled per-field binary encode/decode in pure JS (`DataView`
calls, `BigInt` arithmetic for `DateTimeOffset`, `Map` construction) doesn't beat V8's
highly-optimized native JSON codec. MemoryPack still produces a meaningfully smaller
wire payload (~18% smaller for a 200-row page), which is the actual benefit on this side
of the migration — not parse speed.
