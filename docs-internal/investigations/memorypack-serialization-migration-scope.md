# Investigation: scope of a JSON -> MemoryPack request/response migration

Date: 2026-08-31
Related: [SerializerBenchmark](https://github.com/aminparsa18/SerializerBenchmark)
(external repo - System.Text.Json vs [MemoryPack](https://github.com/Cysharp/MemoryPack)
allocation/throughput numbers that motivated this), ADR-0012 (OTLP-only ingestion),
ADR-0002 (Redis Streams buffering)

The question behind this investigation: after benchmarking MemoryPack against
System.Text.Json (see the linked repo's README), how much of Flare would
actually have to change to serialize `Flare.Api`'s HTTP request/response
bodies with it instead - "do we have to touch every Minimal API endpoint?"
and "what does the dashboard TypeScript side need?" This is scope
measurement, not a decision to switch - no ADR yet, see Follow-ups.

## Method

Read the actual endpoint/DTO/dashboard-client code directly (not just the
`graphify-out/` graph - a first query came back truncated at 2,000 tokens
with 1,107 of 1,172 matched nodes cut, so the concrete counts below are from
`grep`/`Read` against source, cross-checked against the graph's high-level
picture rather than replacing it).

## Finding 1: Minimal APIs have no global formatter hook - it's per-call-site

MemoryPack ships an ASP.NET Core formatter integration
(`MemoryPack.AspNetCoreMvcFormatter`, confirmed present on nuget.org and
restored locally at 1.21.4), but that plugs into MVC's `IInputFormatter`/
`IOutputFormatter` content-negotiation pipeline - which only `[ApiController]`
actions run through. Flare.Api has no MVC controllers; every endpoint is a
Minimal API handler that already does **manual, per-call-site JSON wiring**
for AOT safety, e.g. `src/Flare.Api/Endpoints/LogsEndpoints.cs`:

```csharp
request = await JsonSerializer.DeserializeAsync(http.Request.Body, LogsJsonContext.Default.LogSearchRequest, ct);
...
return Results.Json(response, LogsJsonContext.Default.LogSearchResponse);
```

There is no drop-in switch point. A MemoryPack migration means replacing
both the read and write call in every handler, the same way JSON was wired
in originally.

## Finding 2: exact counts

| Surface | Count | In scope? |
|---|---:|---|
| `Map*` endpoint handlers, `src/Flare.Api/Endpoints/*.cs` (23 files) | 61 | Yes |
| `Results.Json(...)` response call sites (Flare.Api) | 51 | Yes - each is a manual edit |
| Feature-scoped `*JsonSerializerContext` files, `src/Flare.Api/Json/` | ~21 | Yes - each becomes `[MemoryPackable]` types |
| `sealed record` DTOs, `src/Flare.Api/Model/*.cs` (28 files) | 96 | Yes (Phase 0 below) |
| `Map*` endpoints, OTLP ingest (`src/Flare.Ingest/Otlp/*.cs`, 3 files) | 3 | **No** - see Finding 3 |
| Dashboard `*-api.ts` client files, `src/dashboard/src/lib/` | 16 (+`api.ts`) | Yes, separately (Finding 4) |
| Live-tail WebSocket endpoint (`LogTailEndpoints.cs`) | 1 | Yes, but a **third**, separate serialization path (Finding 5) |

**Straight answer: essentially all 61 Flare.Api endpoint handlers need a
matching edit** (both directions, since each already does explicit JSON
wiring), plus the ~21 JSON contexts become MemoryPack type declarations,
plus the 16 dashboard client files and their response-parsing code.

## Finding 3: OTLP ingest endpoints are out of scope

`src/Flare.Ingest/Otlp/OtlpHttp{Logs,Metrics,Trace}Endpoints.cs` negotiate
`application/x-protobuf` vs. `application/json` per the OTLP spec itself -
this is the *external* ingestion contract every real OTLP exporter (Serilog,
NLog, the OTel SDK, ...) sends against. Per ADR-0012 ("one protocol in: OTLP
only"), Flare doesn't get to unilaterally add a third content type here;
these three files and their `JsonFormatter.Default.Format(...)` /
`response.ToByteArray()` calls stay untouched by this migration.

## Finding 4: dashboard TypeScript side

`src/dashboard/src/lib/api.ts`'s own header comment states the current
contract plainly: TypeScript `interface`s are **hand-mirrored** against the
C# DTOs and JSON contexts (camelCase properties, string enums via
`UseStringEnumConverter`). [MemoryPack's TypeScript
support](https://github.com/Cysharp/MemoryPack#typescript-and-aspnet-core-formatter)
replaces that whole convention, not just the wire format:

- every hand-written `interface` is replaced by generator-emitted TS classes
  from the `[MemoryPackable]` C# types - a new build-time codegen step, a new
  npm dependency, wired into `npm run build`
- every `response.json()` (implicit via `fetch`) becomes a binary
  `ArrayBuffer` decode through the generated decoder
- casing/naming is no longer Flare's choice - it's whatever [MemoryPack's TS
  type mapping](https://github.com/Cysharp/MemoryPack#typescript-type-mapping)
  and its [naming/casing
  config](https://github.com/Cysharp/MemoryPack#configure-import-file-extension-and-member-name-casing)
  produce, so all 16 files' response-parsing logic changes shape, not just
  its call signature

## Finding 5: the live-tail WebSocket is a third path, untouched by an HTTP-formatter change

`LogTailEndpoints.cs` (`GET /api/logs/tail`, upgraded to a WebSocket) never
calls `Results.Json` - it hand-calls `JsonSerializer.Deserialize`/writes
frames on the socket directly, using `LogTailJsonContext`. Neither an MVC
formatter nor a Minimal-API `Results.*` swap covers this; it needs its own,
separate migration if it's in scope at all.

## Finding 6: the bigger, separate opportunity this question didn't ask about

`Flare.Ingest`'s `ClickHouseFlushWorker`, `RedisStreamLogEventSink`/
`MetricEventSink`/`SpanEventSink`, and `RedisPatternClusterStore` JSON-
serialize `LogEvent`/`MetricEvent`/`SpanEvent`/`PatternClusterRecord` as the
**internal Redis Streams buffer payload** (ADR-0002) - not an HTTP API at
all. It never crosses into the dashboard, so it needs zero TypeScript
changes, and it sees far higher throughput than the Query API (every
ingested event, not occasional UI clicks). If allocation reduction is the
actual goal rather than "make the API MemoryPack," this internal,
fully-Flare-controlled boundary is arguably the higher-ROI, lower-risk place
to start - flagged here, not pursued in this investigation.

## Phase 0: attach `[MemoryPackable]` to the Flare.Api DTOs

Scope: `src/Flare.Api/Model/*.cs` only (the 96 records from Finding 2's
table) - attribute + `partial` modifier, so the source generator runs and
validates every DTO, with **no endpoint yet serializing via MemoryPack**.
Package added: `MemoryPack` 1.21.4 (`Directory.Packages.props` +
`src/Flare.Api/Flare.Api.csproj`).

Records need `partial` added (`public sealed record Foo` ->
`public sealed partial record Foo`) because MemoryPack's source generator
emits its `Serialize`/`Deserialize` implementation into a second partial
declaration of the same type - without `partial`, `[MemoryPackable]` fails
to compile (MEMPACK002).

**This surfaced a real blocker, not just mechanical busywork**: attaching
`[MemoryPackable]` triggers the generator to validate every member's type,
and `SavedView.State`/`SavedViewRequest.State` (`src/Flare.Api/Model/SavedViewModels.cs`)
are deliberately-opaque `System.Text.Json.JsonElement` - the dashboard-owned,
never-parsed-by-Flare.Api saved-view state blob documented in that type's
own remarks. MemoryPack has no native `JsonElement` support (`MEMPACK019`).
Fixed with a small custom formatter
(`src/Flare.Api/Json/JsonElementMemoryPackFormatter.cs`, registered via
`[ModuleInitializer]`) that round-trips the value as raw JSON text - the same
"treat as a string" pattern MemoryPack's own `UriFormatter` uses for `Uri`,
chosen specifically to preserve the existing "opaque, unparsed blob" contract
exactly rather than changing that type's shape. Confirmed by cloning
`Cysharp/MemoryPack` and reading `UriFormatter.cs` /
`IMemoryPackFormatter.cs` directly rather than guessing the formatter API
surface from memory.

Verified: `dotnet build Flare.slnx` succeeds (0 errors), all 530
`Flare.Api.Tests` pass unchanged - this phase has no runtime behavior
change, since nothing calls `MemoryPackSerializer` yet.

## Phase 1: content-negotiated MemoryPack on every request/response endpoint

Decided with the user rather than assumed: **additive, not a replacement**
(a caller that never asks for MemoryPack gets the exact same JSON wire
format Flare.Api always returned), covering **all** request/response
endpoints in one pass rather than one vertical slice first.

New shared helper: `src/Flare.Api/Json/ApiSerialization.cs` -
`WantsMemoryPack(HttpRequest)` (checks `Accept` for
`application/x-memorypack`), `ReadAsync<T>(HttpContext, JsonTypeInfo<T>, ct)`
(MemoryPack if `Content-Type` says so, else the same
`JsonSerializer.DeserializeAsync` path every endpoint already used), and
`Write<T>(HttpContext, T, JsonTypeInfo<T>, statusCode?)` (MemoryPack via
`Results.Bytes` if `Accept` asked for it, else the same
`Results.Json`/`JsonTypeInfo<T>` path every endpoint already used). Every
endpoint still passes its existing feature-scoped `JsonTypeInfo<T>` - the
AOT-safe JSON path is untouched, MemoryPack is a parallel branch, not a
replacement of it.

**Scope actually covered: 58 of the 61 `Map*` endpoints**, not all 61 -
the 3 WebSocket-upgrade endpoints (`LogTailEndpoints`'s `/api/logs/tail`,
`HostStatsEndpoints`'s `/api/resources/host/watch`,
`ResourceGraphEndpoints`'s `/api/resources/watch`) were left on plain JSON,
carried over unmodified. This wasn't a scope-narrowing choice made
unilaterally after the "all 61" decision - it's Finding 5 applied
literally: a persistent WebSocket connection has no per-message `Accept`
header to negotiate against, so "content-negotiate this endpoint" isn't a
coherent instruction for these three. All 51 `Results.Json(...)` call sites
and all 26 `JsonSerializer.DeserializeAsync(http.Request.Body, ...)` call
sites across the other 58 endpoints now go through `ApiSerialization`.
13 handler methods that returned a response but never took an
`HttpContext` parameter before (needed to read `Accept`) had one added -
Minimal API special-parameter binding matches by type, so where in the
parameter list didn't matter.

Verified:
- `dotnet build Flare.slnx` - 0 errors.
- New tests, `src/Flare.Api.Tests/Json/ApiSerializationTests.cs` (pure,
  no infra, same convention as the Endpoints tests): `Accept`-header
  parsing, a JSON round trip unchanged from the old `Results.Json` path, a
  MemoryPack round trip including a requested status code, a
  `Content-Type`-driven MemoryPack request read, and - the one case Phase 0
  had to special-case - `SavedView.State`'s opaque `JsonElement` surviving
  the *full* negotiated `Write` path, not just a bare
  `MemoryPackSerializer` call.
- Full solution: `dotnet test Flare.slnx` - 781 tests pass (541
  `Flare.Api.Tests`, up from 530 + the 11 new `ApiSerializationTests`; 179
  `Flare.Ingest.Tests`; 61 `Flare.Identity.Tests`), 0 failures.

## Phase 1 live e2e pass (2026-09-01) - found and fixed a real bug

`docker compose up -d --build clickhouse redis api` (real ClickHouse +
Redis + `Flare.Api`, no mocks), then exercised it directly:

- **Baseline unchanged**: `POST /api/logs/search` with plain
  `Content-Type: application/json` and no `Accept` override -
  `{"events":[],"nextCursor":null}`, exactly the pre-migration response.
- **Response negotiation, live**: same request with
  `Accept: application/x-memorypack` on both `POST /api/logs/search` and
  `GET /api/alerts` (the latter one of the 13 handlers that gained an
  `HttpContext` parameter in this phase) - both came back
  `Content-Type: application/x-memorypack` with a compact binary body (9
  and 5 bytes respectively, vs. 32/12 bytes of JSON text for the same empty
  results), confirming the negotiation logic and the parameter-addition
  pattern both work against a real server, not just in unit tests.
- **Request negotiation, live, full round trip**: a throwaway client
  (`MemoryPackSerializer.Serialize` on a real `LogSearchRequest`, `dotnet
  run` against the live container, referencing `Flare.Api.csproj` directly
  for the exact wire types) POSTed a MemoryPack-encoded body with both
  `Content-Type` and `Accept` set to `application/x-memorypack` -
  server logs confirmed it was received as `109` bytes of
  `application/x-memorypack`, decoded successfully, and answered with a
  9-byte MemoryPack response the client decoded back correctly.

**Found a real bug this way, not in any unit test**: a malformed
MemoryPack request body (`curl --data-binary "garbage..."` with
`Content-Type: application/x-memorypack`) returned an unhandled **500**,
not the clean 400 a malformed JSON body already gets. Server logs showed
why: `MemoryPack.MemoryPackSerializationException` propagating out of
`ApiSerialization.ReadAsync`, uncaught, because all 26 existing
`catch (JsonException ex)` blocks (written before MemoryPack existed here)
only know about `System.Text.Json`'s exception type. **Fixed** by having
`ReadAsync<T>` itself catch `MemoryPackSerializationException` and rewrap
it as `JsonException` - every existing catch site keeps working unmodified
rather than 26 files each needing to learn about a second exception type.
Added a regression test
(`ApiSerializationTests.ReadAsync_MalformedMemoryPackBody_ThrowsJsonException`)
and re-verified live: same malformed body now returns a clean
`400 Bad Request` with a `Results.Problem` body, matching JSON's existing
behavior exactly. Re-ran the full valid round trip afterward to confirm
the fix didn't regress the happy path - still passes.

This is the concrete case for why "build + unit tests pass" and "verified
against a live server" are different claims - the gap here was exactly the
kind unit tests miss when every unit test constructs a *valid* payload.

## Conclusion

A JSON -> MemoryPack switch for `Flare.Api`'s HTTP surface touches all 61
endpoint handlers (both request and response, since Minimal APIs have no
global formatter hook), all ~21 JSON contexts, and all 16 dashboard API
client files plus their response-parsing convention - not a subset. OTLP
ingest (3 files) is out of scope by protocol contract. The live-tail and
two Resources-page WebSocket endpoints are a separate, third path. The
internal `Flare.Ingest` -> Redis Streams payload is a distinct, arguably
higher-value target that was never part of "the API" in the first place.
Phase 0 (attribute + `partial` on the 96 `Flare.Api/Model` DTOs) and Phase 1
(content-negotiated MemoryPack on the 58 non-WebSocket endpoints, JSON
kept as the unconditional default) are both done, both compile clean, both
pass their full test suites, and **both are now confirmed against a real,
running `Flare.Api` over ClickHouse + Redis** - not just build/unit-test
green. The live pass caught one real bug (malformed-MemoryPack-body 500,
fixed, regression-tested) that no unit test had, specifically because
every unit test happened to construct a valid payload. **The dashboard was
not touched** - it never sends the MemoryPack `Accept`/`Content-Type`, so
every existing call continues getting JSON exactly as before.

## Unresolved / follow-ups

- ~~No ADR written.~~ Written: [`../adr/0015-memorypack-content-negotiation-for-flare-api.md`](../adr/0015-memorypack-content-negotiation-for-flare-api.md) -
  documents the additive/all-endpoints-at-once shape, the alternatives
  weighed, and the malformed-body bug this investigation's live e2e pass
  found as a Consequence.
- Live e2e covered `POST /api/logs/search` and `GET /api/alerts` only (one
  request-body endpoint, one response-only endpoint) - not all 58. The bug
  it found was in the shared `ApiSerialization.ReadAsync` helper every
  endpoint uses identically, so it's reasonable to expect the fix
  generalizes, but the other 56 endpoints' live behavior wasn't
  individually exercised.
- Not investigated here: whether MemoryPack's "Version tolerant" mode
  (schema evolution) is adopted, and what that costs in generator config.
- Not investigated/decided here: whether the dashboard TypeScript side ever
  actually adopts the MemoryPack `Accept`/`Content-Type` (Finding 4) - Phase
  1 makes it possible server-side without committing to it.
- Not investigated here: the three WebSocket endpoints' migration shape
  (Finding 5), or whether it's even worth moving given they're low-volume
  control/push channels compared to the Query API's request/response
  traffic.
- Not pursued here, flagged as the likely better first target once Phase 1
  is confirmed working end-to-end: the internal `Flare.Ingest` Redis
  Streams payload (Finding 6) - a self-contained, dashboard-independent,
  higher-throughput boundary Flare fully controls on both ends. Add to
  `docs-internal/planning/roadmap.md` once Phase 1's live e2e pass above is
  done, not before (explicit user instruction, 2026-09-01).
- `src/Flare.Ingest`'s own DTOs (`LogEvent`/`MetricEvent`/`SpanEvent`/
  `PatternClusterRecord`/`IngestionErrorEntry`) were not attributed in
  either phase - both were scoped to `Flare.Api` only, per Finding 6's
  reasoning that Ingest is a separate track.
