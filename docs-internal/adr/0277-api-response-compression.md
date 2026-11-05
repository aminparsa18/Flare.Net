# ADR-0277: gzip-1 response compression in Flare.Api

Status: Accepted

Date: 2026-11-05

Evidence: [investigations/serialization-end-to-end-benchmark.md](../investigations/serialization-end-to-end-benchmark.md),
run [benchmarks/2026-11-04/](../benchmarks/2026-11-04/report.html).

## Context

Flare.Api compressed no responses. The end-to-end serialization benchmark found that compression was
the largest effect it measured, larger than the choice between JSON and MemoryPack. The dashboard's
production path is MemoryPack, uncompressed. At 100 Mbps that path is 2.6–3.8× slower on full-size
pages than the best compressed option, and up to 11.5× slower at 5 Mbps. Even at 1 Gbps it is 1.1–1.6×
slower. Compressing a page costs about 0.2 ms of server CPU at gzip-1.

Four questions had to be settled: which codec and level, which content types, whether to compress
over HTTPS given BREACH, and whether the reverse proxy should do it instead.

## Decision

Flare.Api runs ASP.NET Core's built-in `UseResponseCompression`, registered by
`FlareResponseCompression` in `src/Flare.Api/Hosting/ResponseCompression.cs`. It sits first in the
pipeline after the base-path middleware.

### Codec and level: gzip at `CompressionLevel.Fastest` (gzip 1)

From the 2026-11-04 run, end to end (server encode and compress, modeled transfer, client
decompress, decode and map), gzip-1 compared with the best of gzip-1, gzip-6 and brotli-4:

| Link | gzip-1 vs the best codec | gzip-1 vs uncompressed |
|---|---|---|
| 1 Gbps | best in every full-size cell | 1.0–1.6× faster |
| 100 Mbps | within 3% for MemoryPack, within 14% for JSON | 2.3–5.8× faster |
| 20 Mbps | 1.1–1.5× slower | 3.1–9.4× faster |
| 5 Mbps | 1.2–1.9× slower | 3.3–10.7× faster |

The extra CPU of gzip-6 and brotli-4 (0.4–1.7 ms per full-size page) only pays off at about 20 Mbps
and below. Localhost, a datacenter LAN and most office networks are in the first two rows, so the
default is the level that wins there. On the 4-span trace (about 2–3 kB), compressing costs about
10 µs at 1 Gbps, which is noise.

Only the gzip provider is registered. Browsers send `Accept-Encoding: gzip, deflate, br, zstd`, and
the framework default would pick Brotli at quality 4, the level that loses on fast links.

`ResponseCompression:Level=Optimal` (gzip 6) is the setting for deployments reached mostly over slow
links. On the benchmark's 20 Mbps and 5 Mbps cells, gzip-6 comes within 13% of brotli-4. That is too
small a gain to justify a second provider and a second setting.

### Content types

The framework defaults (JSON, text, XML, JavaScript, CSS, wasm) plus:

- `application/x-memorypack`, the dashboard's format (ADR-0015). Without it, the main traffic would
  stay uncompressed.
- `text/csv` and `application/x-ndjson`, used by the audit and data-subject exports. These are the
  largest bodies Flare.Api writes.

`text/event-stream` is deliberately not included: the MCP endpoint's SSE stream has to reach the client
one event at a time, not sit in a compressor's buffer. A response without a `Content-Type` is never
compressed, so WebSocket upgrades (live tail, `/watch` endpoints) are unaffected. Tempo's
`application/protobuf` responses are left out too: they are small, and only Grafana reads them.

Clients that send no `Accept-Encoding` get the uncompressed body, exactly as before. That includes
.NET `HttpClient` by default, so the `flare` CLI and the MCP tools' loopback calls into `/api/*` spend
no CPU on compression.

### `EnableForHttps = true`

The framework leaves compression off for HTTPS by default because of BREACH. A BREACH attack needs
all of the following:

1. A secret in the response body that stays the same from one response to the next.
2. Attacker-chosen text reflected in the same body.
3. The ability to make the victim's browser send many authenticated requests.
4. A position on the network to observe the encrypted response lengths.

Flare.Api fails the first and third conditions:

- **No stable secret in a body.** There are no anti-forgery tokens; the session travels in an
  `HttpOnly` cookie, which is a header. Secrets returned on purpose (a new personal access token,
  ingest key or service-account token) are generated fresh for each response, so there is nothing
  fixed to converge on. Stored secrets never come back: notification channels mask theirs, and the
  Entra, OIDC and LDAP settings return only `HasClientSecret`/`HasBindPassword`.
- **A cross-site page cannot drive authenticated requests.** The session cookie is `SameSite=Lax` by
  default, so it is not sent on cross-site `fetch`, form `POST` or subresource requests. The query
  endpoints, which return attacker-influenced data, are `POST`. Bearer-token clients (PATs, the CLI,
  MCP, Grafana) cannot be driven by a third-party page at all.

`EnableForHttps=false` would also protect less than it appears to. Most deployments terminate TLS at
a reverse proxy, so Kestrel sees plain HTTP, and the flag does not apply.

**Residual risk.** Telemetry can contain secrets, for example a token in a log line. A deployment that
sets `Auth__CookieSameSite=None` lets a cross-site page send credentialed "simple" `POST`s. The
`text/plain` content type avoids a CORS preflight, and Flare reads such a body as JSON. If such a
deployment also treats an on-path observer as part of its threat model, it should set
`ResponseCompression__Enabled=false`. The how-to page says so.

### In the application, not only at the proxy

Flare is often run with no proxy at all: `docker compose up`, the `flare` CLI and Aspire. Doing it in
Flare.Api covers every install path. A proxy in front still works:

- nginx compresses proxied responses only when `gzip_proxied` allows it, and passes an
  upstream-encoded body through unchanged.
- Caddy's `encode` and Traefik's `compress` skip responses that already carry `Content-Encoding`.

`ResponseCompression__Enabled=false` hands the job to the proxy completely, for example to use zstd
or Brotli there.

## Consequences

- Dashboard responses shrink by about 85–90% (gzip-1 on the captured pages: logs 88.5 → 10.8 kB in
  MemoryPack). Most of the request time on remote links goes away.
- About 0.1–0.3 ms of server CPU per full-size response. Responses also carry
  `Vary: Accept-Encoding`. Compression removes `Content-Length`, so bodies go out chunked (HTTP/1.1)
  or as DATA frames (HTTP/2).
- The server-stage benchmark (`ServerStageBenchmarks`) now runs its compressed cases through the real
  `ResponseCompressionMiddleware`, with Flare's registration for gzip-1 and gzip-6. A benchmark run
  therefore measures the shipped path.
- Doing that showed that the middleware's JSON bodies are slightly larger than one-shot compression of
  the same bytes. `Results.Json` flushes partway through the body, and the middleware turns each
  flush into a deflate or Brotli flush block. The overhead is 0.4–1.4% at gzip-1, up to 4.5% at
  gzip-6 and up to 6.7% at brotli-4. MemoryPack is written in one go and comes out byte-identical.
  The overhead grows with the level, so it is one more point in favour of gzip-1. The report's
  transfer model still uses the one-shot fixture sizes.
- Checked live against a running Flare.Api:
  - `/api/logs/search` returns `Content-Encoding: gzip` and `Vary: Accept-Encoding` for both JSON and
    MemoryPack.
  - A request without `Accept-Encoding` gets the plain body.
  - The live-tail WebSocket upgrade still returns `101` with no `Content-Encoding`.
- Not done here: MemoryPack still builds the whole body as one `byte[]` before writing (Finding 5 of
  the investigation). Compression does not change that.

## Re-run

The full suite was re-run on 2026-11-05 ([benchmarks/2026-11-05/](../benchmarks/2026-11-05/report.html)),
on the same machine, with the server stage going through the real middleware.

- **The decision holds.** gzip-1 is the best codec in every 1 Gbps cell with a full-size page, except
  the noisy cell described below. At 100 Mbps it is within 13% of the best on full-size pages, and it is
  2.1–5.7× faster than uncompressed there.
- **The middleware's overhead is small.** It adds 0.7–0.9 kB of allocation per response. MemoryPack
  server-stage times are within ±4% of the bare-stream run of 2026-11-04.
- **JSON server-stage times are 5–9% slower, but that is drift between the two runs.** The
  uncompressed JSON cases, which never touch the middleware, moved by +2% to +8% as well.
- **Outliers:**
  - `trace-large.synthetic` JSON gzip-1 is 17% slower. The laptop slept briefly while that case was
    being measured. Its neighbours, the same scenario at gzip-6 and brotli-4, moved by +4% and +5%.
    It was not re-measured.
  - On `logs-page.synthetic` MemoryPack, uncompressed beats gzip-1 by 10% at 1 Gbps. That comes from
    the Node client stage alone: decompress went from 0.45 ms to 0.88 ms, though that code did not
    change. The server stage for that cell matches 2026-11-04.
