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

## Conclusion

A JSON -> MemoryPack switch for `Flare.Api`'s HTTP surface touches all 61
endpoint handlers (both request and response, since Minimal APIs have no
global formatter hook), all ~21 JSON contexts, and all 16 dashboard API
client files plus their response-parsing convention - not a subset. OTLP
ingest (3 files) is out of scope by protocol contract. The live-tail
WebSocket is a separate, third path. The internal `Flare.Ingest` ->
Redis Streams payload is a distinct, arguably higher-value target that was
never part of "the API" in the first place. Phase 0 (attribute + `partial`
on the 96 `Flare.Api/Model` DTOs) is done, compiles clean, and already paid
for itself by catching the `JsonElement` incompatibility before it became a
runtime surprise mid-migration.

## Unresolved / follow-ups

- No ADR yet - per `docs-internal/README.md`'s ADR bar, actually switching
  the wire format is a wire-compatibility-affecting decision with real
  alternatives (this investigation exists partly to inform that ADR, not
  replace it). Write one before Phase 1 touches any endpoint's actual
  request/response wiring.
- Not investigated here: whether MemoryPack's "Version tolerant" mode
  (schema evolution) is adopted, and what that costs in generator config -
  relevant once real endpoints move off `JsonSerializerContext`.
- Not investigated here: the live-tail WebSocket's migration shape (Finding
  5), or whether it's even worth moving given it's a small, low-volume
  control-message channel compared to the Query API responses.
- Not pursued here, flagged as the likely better first target: the internal
  `Flare.Ingest` Redis Streams payload (Finding 6) - a self-contained,
  dashboard-independent, higher-throughput boundary Flare fully controls on
  both ends.
- `src/Flare.Ingest`'s own DTOs (`LogEvent`/`MetricEvent`/`SpanEvent`/
  `PatternClusterRecord`/`IngestionErrorEntry`) were not attributed in this
  pass - Phase 0 was scoped to `Flare.Api/Model` only, per Finding 6's
  reasoning that they're a separate track.
