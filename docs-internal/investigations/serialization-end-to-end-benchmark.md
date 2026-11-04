# Investigation: JSON vs MemoryPack end to end, with compression

Date: 2026-11-04
Run: [docs-internal/benchmarks/2026-11-04/](../benchmarks/2026-11-04/report.html) (open
`report.html` for the charts; `results.json` holds every number below)
Related: ADR-0015 (MemoryPack content negotiation for Flare.Api), ADR-0016/ADR-0276 (dashboard
MemoryPack decoders), ADR-0017 (MemoryPack for the Ingest Redis buffer). Supersedes Findings 1, 2 and 4
of [memorypack-vs-json-benchmark.md](memorypack-vs-json-benchmark.md).

The earlier investigation measured each codec in isolation. It found that MemoryPack is faster in .NET,
JSON is faster in V8, and MemoryPack is about 18% smaller. It left three questions open, and this run
answers them:

1. End to end (server encode, transfer, browser decode), does MemoryPack still pay off for the
   dashboard?
2. Does MemoryPack's size advantage survive gzip or Brotli, and is compression worth its server CPU at
   different link speeds? Flare.Api does not compress responses today.
3. Do the earlier numbers hold on real payloads and with the JSON System.Text.Json actually sends?

This is measurement only. No production behaviour changed. Changing the dashboard's format, or enabling
response compression, would be a decision with its own ADR.

## Method

The code is in `src/Flare.Benchmarks` and `src/dashboard/bench`; see that project's README. In
summary:

- **Payloads.** Three scenarios were captured from a stack seeded with `examples/ExampleApp.Seeder`:
  - `logs-page.captured`: 200 rows from `POST /api/logs/search`.
  - `trace.captured`: the largest seeded trace, 4 spans.
  - `metrics.captured`: `http.server.request.duration`, p50–p99 by `url.path`, top 10, 30 s
    buckets, 484 points.

  Two are synthetic:
  - `logs-page.synthetic`: 200 rows with the modeled OTel attribute bags of the earlier run.
  - `trace-large.synthetic`: 200 captured spans re-parented into one trace, because seeded traces
    are tiny.

  Each scenario is one production object, encoded by each codec in .NET. The TypeScript side decodes
  those exact bytes.
- **Server stage.** BenchmarkDotNet runs the production `ApiSerialization.Write` →
  `IResult.ExecuteAsync` path into an in-memory response, optionally through a gzip or Brotli stream.
  This is the same body wrapping `UseResponseCompression` does. The codecs are gzip-1, gzip-6 and
  brotli-4, which on .NET 10 are `Fastest`, `Optimal` and Brotli `Optimal`.
- **Client stage.** tinybench runs under Node 26.5 / V8 14.6, the engine Chrome and Edge use. Each run
  takes the received bytes, decompresses them with native zlib, decodes them, and maps them into the
  types `searchLogs`, `getTrace` and `queryMetric` return.
  - MemoryPack uses the generated decoders plus the production mappers.
  - JSON uses `TextDecoder` + `JSON.parse` + a field-by-field mapper.
  - Both paths are checked to produce identical UI data before anything is timed.
- **Transfer** is modeled as `bytes × 8 ÷ bandwidth` at 1 Gbps, 100 Mbps, 20 Mbps and 5 Mbps. RTT,
  TCP slow start and protocol overhead are not modeled.
- **Statistics.** All figures are medians. The machine is an Intel i5-1038NG7 running macOS 26.5,
  .NET 10.0.0 and BenchmarkDotNet 0.15.8. Server and client ran on the same machine.

## Finding 1: compression matters more than the choice of format

Today's production path is MemoryPack with no compression. Every other combination was compared
against it:

| Scenario | Link | Today: MemoryPack, uncompressed | Fastest combination | JSON + gzip-1 |
|---|---|---:|---|---:|
| logs-page.captured | 1 Gbps | 2.11 ms | JSON · gzip-1, 1.33 ms | 1.33 ms |
| | 100 Mbps | 8.49 ms | JSON · gzip-6, 2.38 ms | 2.55 ms |
| | 20 Mbps | 36.8 ms | MemoryPack · brotli-4, 4.91 ms | 7.96 ms |
| | 5 Mbps | 143 ms | MemoryPack · brotli-4, 13.8 ms | 28.3 ms |
| trace-large.synthetic | 100 Mbps | 8.72 ms | JSON · gzip-6, 2.32 ms | 2.35 ms |
| | 5 Mbps | 148 ms | JSON · brotli-4, 12.9 ms | 23.8 ms |
| metrics.captured | 100 Mbps | 7.47 ms | MemoryPack · gzip-1, 2.86 ms | 4.27 ms |
| | 5 Mbps | 136 ms | MemoryPack · gzip-6, 30.8 ms | 52.3 ms |
| logs-page.synthetic | 100 Mbps | 17.1 ms | JSON · gzip-6, 6.47 ms | 7.38 ms |
| | 5 Mbps | 300 ms | JSON · brotli-4, 61.3 ms | 103 ms |

The fastest combination is a compressed one in 19 of the 20 scenario × link cells. The exception is the
4-span trace at 1 Gbps, where everything takes about 43 µs.

Against today's path, the best combination is:

| Link | Faster by (200-row and 484-point payloads) | 4-span trace |
|---|---|---|
| 1 Gbps | 1.1–1.6× | about the same |
| 100 Mbps | 2.6–3.8× | 1.8× |
| 20 Mbps | 3.8–8.3× | 2.7× |
| 5 Mbps | 4.4–11.5× | 3.2× |

Uncompressed, transfer is most of the request even at 100 Mbps. For the captured logs page, JSON
uncompressed spends 12.6 ms of its 13.3 ms in transfer.

Compression is cheap by comparison. On the captured logs page, JSON with gzip-1 costs 0.18 ms of server
CPU and 0.29 ms of client decompression, and it cuts the body from 157 kB to 16.9 kB, 89% smaller.
MemoryPack with gzip-1 goes from 88.5 kB to 10.8 kB, 88% smaller.

gzip-6 and brotli-4 cost 0.4–1.7 ms of server CPU per full-size response. That extra cost pays off
only at 20 Mbps and below; at 1 Gbps, gzip-1 is the fastest choice for every full-size payload.

A Flare deployment on localhost or a datacenter LAN sees the 1 Gbps column. A remote user, on a VPN or
on Wi-Fi, sees the 100 Mbps column or slower.

## Finding 2: MemoryPack's size advantage mostly disappears under compression

| | Uncompressed | gzip-1 | gzip-6 | brotli-4 |
|---|---:|---:|---:|---:|
| MemoryPack vs JSON size, range across scenarios | −44% to −21% | −36% to −14% | −17% to +1% | −22% to +6% |

Uncompressed, MemoryPack is 21–44% smaller on these payloads, more than the 18% the earlier run
measured. The captured rows carry fewer attributes, so a larger share of JSON is field names.

At gzip-6 or brotli-4 the gap is −17% to +6%, and on the synthetic logs page JSON is slightly
*smaller*. JSON's repeated field names are exactly what a compressor removes.

## Finding 3: per-stage costs point in opposite directions in .NET and V8

**.NET** (`api-codec`): MemoryPack encodes 2.4–2.7× and decodes 2.9–4.9× faster on log and trace pages.
On the metrics response it encodes 22.7× and decodes 31.1× faster: 29 µs vs 661 µs to encode, and 26 µs
vs 811 µs to decode. That payload is mostly `double`s, which MemoryPack copies as raw bytes and which
System.Text.Json has to format and parse as shortest-round-trip text.

The Redis buffer (ADR-0017) confirms the earlier numbers on one event: MemoryPack is 1.4–2.0× faster
to encode and 2.3–3.2× faster to decode. A `LogSearchRequest` body decodes in 0.4 µs with MemoryPack and
3.0 µs with JSON.

**V8** (`decode`): going from bytes to UI types is slower with MemoryPack in every scenario:

| Scenario | JSON (decode + map) | MemoryPack (decode + map) | MemoryPack ÷ JSON |
|---|---:|---:|---:|
| logs-page.captured | 0.44 ms | 1.32 ms | 2.97× |
| logs-page.synthetic | 0.64 ms | 2.12 ms | 3.31× |
| trace-large.synthetic | 0.50 ms | 1.25 ms | 2.48× |
| metrics.captured | 0.49 ms | 0.61 ms | 1.23× |
| trace.captured | 11.6 µs | 25.2 µs | 2.17× |

This corrects the earlier Finding 2, whose JSON side was a `JSON.stringify` of TypeScript objects
decoded into `Date`/`bigint`. On real System.Text.Json output mapped to the real UI types, JSON's mapping
costs almost nothing, because the UI keeps timestamps as ISO strings. The MemoryPack path pays for the
generated decoder plus the production mappers' `toISOString()` and `Number(bigint)` conversions:
decode-only is 0.94 ms and decode+map is 1.32 ms on the captured logs page.

Metrics is the only case where the MemoryPack decoder alone beats `JSON.parse` (0.22 ms vs 0.48 ms). The
mapping step then takes most of that lead back.

Encoding a request body is a wash in V8: 1.5 µs with JSON and 1.7 µs with MemoryPack.

## Finding 4: end to end, each format at its best codec wins half the cells

With each format at its best codec, JSON wins 10 of the 20 cells and MemoryPack wins 10.

| Scenario | 1 Gbps | 100 Mbps | 20 Mbps | 5 Mbps |
|---|---:|---:|---:|---:|
| logs-page.captured | JSON by 30% | JSON by 5% | MP by 5% | MP by 9% |
| logs-page.synthetic | JSON by 28% | JSON by 13% | JSON by 6% | JSON by 4% |
| trace-large.synthetic | JSON by 36% | JSON by 12% | JSON by 8% | JSON by 7% |
| metrics.captured | MP by 38% | MP by 32% | MP by 12% | MP by 5% |
| trace.captured (4 spans) | MP by 1% | MP by 8% | MP by 11% | MP by 19% |

Each cell compares the best combination of one format against the best of the other.

Once responses are compressed, the size difference is small, so the per-stage costs from Finding 3 decide
the result:

- **JSON wins log and trace pages on fast links** because V8 decodes it faster.
- **MemoryPack wins numeric metrics payloads** because of its large .NET encode advantage.
- **MemoryPack wins tiny payloads, and logs on slow links,** where its few hundred bytes of size
  advantage still matter.

The margins are much smaller than the gap between compressed and uncompressed (Finding 1).

## Caveats

- **Seeded data is light.** Captured log rows carry 3 resource attributes and 1 log attribute.
  Production OTel data usually carries more. The synthetic logs page is the heavier stand-in, and it
  favours JSON more.
- **Trace size is synthetic.** Seeded traces have at most 4 spans, so the large trace is assembled from
  captured spans. The spans are real; only the tree is synthetic.
- **The client is Node/V8, not a browser.** JavaScriptCore (Safari) and SpiderMonkey (Firefox) were not
  measured. Real clients are often slower than this laptop, which scales the client stages (decode +
  map) up and favours whichever format is cheaper there, which is JSON for everything but metrics.
- **The transfer model is a lower bound.** It ignores RTT and TCP slow start. Both formats pay those
  equally, but they make small responses relatively more latency-bound than shown.
- **Server and client ran on the same machine**, a laptop CPU. One run, no repetitions across days.

## Consequences / follow-ups

- No ADR changes here. ADR-0015/0016/0017/0276 stand as written. The evidence bears on two possible
  future decisions; neither is made here:
  - **Enabling response compression in Flare.Api** has the largest effect measured: 2.6–3.8× faster at
    100 Mbps and up to 11.5× at 5 Mbps on the full-size payloads, and 1.1–1.6× even at 1 Gbps.
  - **Whether the dashboard should keep MemoryPack** comes out roughly even once responses are
    compressed: it is better for metrics and worse for log and trace pages on fast links. Separately,
    upstream Cysharp has said the MemoryPack TypeScript generator will be removed and that MemoryPack
    is for trusted data only.
- If compression is enabled through `UseResponseCompression`:
  - its default `MimeTypes` do not include `application/x-memorypack`, so MemoryPack responses would
    stay uncompressed unless that type is added;
  - `EnableForHttps` defaults to false because of BREACH-style concerns with secrets in compressed
    responses, so that trade-off would need to be assessed in the ADR.
- The benchmarks are repeatable with `scripts/bench.sh`. Each run is stored under
  `docs-internal/benchmarks/` and compared with the previous one automatically.
