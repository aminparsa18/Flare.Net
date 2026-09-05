# ADR-0015: MemoryPack as an opt-in, content-negotiated wire format for `Flare.Api`

Status: Accepted
Date: 2026-09-01

## Context

An external benchmark
([SerializerBenchmark](https://github.com/aminparsa18/SerializerBenchmark))
measured [MemoryPack](https://github.com/Cysharp/MemoryPack) against
`System.Text.Json` and found meaningfully lower allocation and higher
throughput. The question that followed - how much of `Flare.Api` would
actually have to change to serve MemoryPack - was answered in
[`../investigations/memorypack-serialization-migration-scope.md`](../investigations/memorypack-serialization-migration-scope.md)
before any code changed, and that investigation's findings are this ADR's
Context:

- **Minimal APIs have no MVC-style formatter pipeline to hook globally.**
  MemoryPack ships an ASP.NET Core formatter
  (`MemoryPack.AspNetCoreMvcFormatter`) that plugs into
  `IInputFormatter`/`IOutputFormatter` content negotiation - but that
  pipeline is an MVC-controller feature. `Flare.Api` has no `[ApiController]`
  actions; every route is a Minimal API handler that already does manual,
  per-call-site JSON wiring for AOT safety (`JsonSerializer.DeserializeAsync`
  on the way in, `Results.Json` on the way out, both driven by a
  feature-scoped `JsonSerializerContext`). There is no drop-in switch point -
  any MemoryPack support has to be wired the same explicit way JSON already
  is, at every call site.
- **61 `Map*` endpoints, 51 `Results.Json` call sites, ~21 JSON contexts, 16
  dashboard API client files** - not a subset touchable in isolation, since
  every endpoint's request and response are wired independently.
- **3 of those 61 endpoints are WebSocket upgrades**
  (`LogTailEndpoints`'s `/api/logs/tail`, `HostStatsEndpoints`'s
  `/api/resources/host/watch`, `ResourceGraphEndpoints`'s
  `/api/resources/watch`) - a persistent connection has no per-message
  `Accept` header to negotiate against, so "content-negotiate this
  endpoint" isn't a coherent instruction for them.
- **OTLP ingestion (3 `Flare.Ingest` endpoints) is out of scope by protocol
  contract** - see ADR-0012 ("one protocol in: OTLP only"). Flare doesn't
  control what real OTLP exporters send.
- **`Flare.Ingest`'s internal Redis Streams buffer payload**
  (`LogEvent`/`MetricEvent`/`SpanEvent`/`PatternClusterRecord`, ADR-0002) is
  a separate, self-contained, dashboard-independent boundary that was never
  part of "the API" - a distinct, likely higher-throughput target, deferred
  to `../planning/roadmap.md` rather than folded into this decision.

Two questions this ADR settles that the investigation deliberately left
open: **breaking or additive**, and **one vertical slice or everything at
once**.

## Decision

**Additive, not a replacement: JSON stays Flare.Api's unconditional
default.** A caller opts into MemoryPack per-request via
`Accept`/`Content-Type: application/x-memorypack`; a caller that never
sends that content type - which is every caller today, including the
dashboard - gets exactly the JSON wire format `Flare.Api` always returned,
serialized through the same feature-scoped `JsonTypeInfo<T>` contexts
unchanged. Nothing about the existing JSON contract changes shape, casing,
or behavior.

**All 58 non-WebSocket request/response endpoints in one pass, not a
vertical slice.** Every `Results.Json` call site and every
`JsonSerializer.DeserializeAsync(http.Request.Body, ...)` call site across
those 58 endpoints now routes through one shared helper,
`Flare.Api.Json.ApiSerialization`:

- `WantsMemoryPack(HttpRequest)` - `Accept` header check.
- `ReadAsync<T>(HttpContext, JsonTypeInfo<T>, ct)` - MemoryPack if
  `Content-Type` says so, else the original `JsonSerializer.DeserializeAsync`
  path.
- `Write<T>(HttpContext, T, JsonTypeInfo<T>, statusCode?)` - MemoryPack via
  `Results.Bytes` if `Accept` asked for it, else the original
  `Results.Json` path.

The 3 WebSocket endpoints were left on plain JSON, unmodified - consistent
with the Context above, not a narrowing of this decision.

**DTOs gain `[MemoryPackable]` + `partial`, not a parallel type
hierarchy.** All 96 `sealed record` types in `src/Flare.Api/Model/` are
directly MemoryPack-serializable; the one type that couldn't be
(`SavedView.State`/`SavedViewRequest.State`, a deliberately-opaque
`JsonElement` never parsed by `Flare.Api` - see `SavedViewModels.cs`'s
remarks) got a small hand-written `MemoryPackFormatter<JsonElement>`
(`Json/JsonElementMemoryPackFormatter.cs`) that round-trips it as raw JSON
text, preserving its existing "opaque blob" contract exactly rather than
changing that type's shape.

**The dashboard is untouched.** It never sends the MemoryPack
`Accept`/`Content-Type`, so this decision has zero effect on it today -
whether the dashboard ever adopts MemoryPack (Finding 4 of the
investigation: hand-mirrored TypeScript interfaces would be replaced by
MemoryPack's generated TS decoders, a real build-tooling and convention
change) is an explicitly separate, not-yet-made decision.

## Alternatives considered

- **Full replacement of JSON with MemoryPack on touched endpoints.**
  Rejected: breaking. The dashboard (and anything else calling these
  routes) would stop working the moment an endpoint was migrated, until
  its client was migrated in lockstep - a much larger, riskier, harder-to-
  reverse change for no benefit over the additive approach, since nothing
  about MemoryPack requires abandoning JSON support.
- **One vertical slice first** (e.g. just `LogsEndpoints` end-to-end,
  proving the pattern before touching the other 55). Considered and
  rejected in favor of covering all 58 at once: the per-endpoint change is
  small and mechanical once the shared helper exists (swap one call for
  another, occasionally add an `HttpContext` parameter), so the marginal
  risk of doing all 58 together was judged low relative to the cost of
  re-reviewing and re-verifying the same pattern in 58 separate follow-up
  passes. This traded a larger single diff for fewer total review/verify
  cycles - the live e2e pass below was the check on that trade, and it did
  catch a real bug (see Consequences), which is exactly the scenario this
  alternative was meant to guard against; the bug lived in the one shared
  helper every endpoint uses identically, so fixing it once fixed it
  everywhere rather than needing 58 separate fixes.
- **A parallel `MemoryPackSerializerContext`-equivalent type hierarchy**,
  keeping `Flare.Api/Model` JSON-only and duplicating types for MemoryPack.
  Rejected: MemoryPack attaches via attributes on the existing types with no
  behavioral change to their JSON serialization, so duplicating them would
  add a second set of DTOs to keep in sync for no benefit.
- **Wait for MemoryPack's ASP.NET Core MVC formatter to become usable**
  (e.g. by converting endpoints to MVC controllers). Rejected outright -
  converting `Flare.Api`'s entire Minimal API surface to MVC controllers to
  gain a formatter-pipeline hook would be a far larger, unrelated
  architectural change than this ADR is scoped to make.

## Consequences

- New dependency: `MemoryPack` 1.21.4 (`Directory.Packages.props` +
  `src/Flare.Api/Flare.Api.csproj`).
- New shared file `src/Flare.Api/Json/ApiSerialization.cs` is now the one
  seam every request/response endpoint's serialization goes through -
  future endpoints should call it rather than reintroducing direct
  `JsonSerializer`/`Results.Json` calls, to keep MemoryPack support uniform.
- `ApiSerialization.ReadAsync<T>` rewraps
  `MemoryPack.MemoryPackSerializationException` as `System.Text.Json.JsonException`
  on a malformed MemoryPack body. This was not an original design choice -
  it was a bug found by a live e2e pass against a real running server
  (malformed MemoryPack input 500'd instead of 400ing, because the 26
  existing `catch (JsonException ex)` blocks predate MemoryPack and only
  know about `System.Text.Json`'s exception type) and fixed by rewrapping
  in the one shared helper rather than teaching 26 files about a second
  exception type. Anyone adding a new endpoint that reads a request body
  gets this handling for free by using `ApiSerialization.ReadAsync`, but
  should not assume `JsonException` literally means "the body was JSON" -
  it now means "the body was malformed, in whichever format was
  requested."
- 13 handler methods that returned a response but never took an
  `HttpContext` parameter before gained one (needed to read `Accept`).
  Minimal API special-parameter binding matches by type, so parameter
  order was not significant.
- Verified live against a real `docker compose up` stack (ClickHouse +
  Redis + `Flare.Api`, no mocks): JSON baseline unchanged, MemoryPack
  response negotiation on two endpoints, a full MemoryPack request+response
  round trip via a throwaway client built against the real
  `Flare.Api.csproj` types, and the malformed-body fix re-confirmed after
  rebuild. Full detail, including the bug found and the exact commands run,
  is in the investigation doc's "Phase 1 live e2e pass" section.
- Full solution (`dotnet test Flare.slnx`) - 782 tests pass, including 12
  new tests in `src/Flare.Api.Tests/Json/ApiSerializationTests.cs`
  (pure/no-infra, same convention as the Endpoints tests) covering both
  serialization directions, status-code handling, the malformed-body
  regression, and `SavedView.State`'s opaque `JsonElement` surviving the
  full negotiated path.
- Not covered by this ADR, left as open follow-ups in the investigation
  doc: whether the dashboard ever adopts the MemoryPack content type; the
  3 WebSocket endpoints' migration shape, if any; MemoryPack's
  "Version tolerant" schema-evolution mode; the internal `Flare.Ingest`
  Redis Streams payload (tracked separately in
  `../planning/roadmap.md`, deliberately not part of this decision).

## Related documentation

- [`../investigations/memorypack-serialization-migration-scope.md`](../investigations/memorypack-serialization-migration-scope.md) -
  the full scope assessment (endpoint/DTO/dashboard counts, findings 1-6),
  the Phase 0/Phase 1 implementation record, and the live e2e pass this
  ADR's Consequences summarize.
- [`../investigations/memorypack-vs-json-benchmark.md`](../investigations/memorypack-vs-json-benchmark.md) -
  project-local benchmark numbers for both sides of this boundary: MemoryPack wins on
  every axis in .NET (Finding 1), but on the dashboard's TypeScript side native
  `JSON.parse`/`JSON.stringify` actually beats MemoryPack's generated decoders on speed
  (Finding 2) - the benefit there is payload size, not parse speed.
- [`0012-otlp-only-ingestion.md`](0012-otlp-only-ingestion.md) - why OTLP
  ingestion is out of scope for this decision.
- [`0002-redis-streams-buffering.md`](0002-redis-streams-buffering.md) -
  the internal buffer payload flagged as a separate, not-yet-decided
  follow-on target (`../planning/roadmap.md`).
