# ADR-0017: MemoryPack, with a tagged upgrade fallback, for `Flare.Ingest`'s internal Redis Streams buffer

Status: Accepted
Date: 2026-09-04

## Context

[ADR-0015](0015-memorypack-content-negotiation-for-flare-api.md) adopted
MemoryPack for `Flare.Api`'s HTTP request/response surface and explicitly
deferred a second target flagged by the investigation behind it
([Finding 6](../investigations/memorypack-serialization-migration-scope.md#finding-6-the-bigger-separate-opportunity-this-question-didnt-ask-about)):
`Flare.Ingest`'s internal Redis Streams buffer (ADR-0002). Four call sites
`System.Text.Json`-serialize a whole record as one blob into a single Redis
Stream/string field:

- `Sinks.RedisStreamLogEventSink` / `Pipeline.ClickHouseFlushWorker` -
  `Model.LogEvent`.
- `Sinks.RedisStreamMetricEventSink` / `Pipeline.MetricFlushWorker` -
  `Model.MetricPointRecord` (a `JsonPolymorphic`/`JsonDerivedType`
  discriminated union over `GaugePointRecord`/`SumPointRecord`/
  `HistogramPointRecord`).
- `Sinks.RedisStreamSpanEventSink` / `Pipeline.SpanFlushWorker` -
  `Model.SpanRecord`.
- `Patterns.RedisPatternClusterStore` - `Patterns.ClusterRecord[]`, one
  bucket's Drain cluster list.

This boundary is materially different from `Flare.Api`'s HTTP surface in
ways that change what "migrate it" should mean:

- **It never crosses into the dashboard.** Nothing outside this process
  (and Redis, as a passive byte store) ever reads these bytes, so there is
  no external caller to keep backward-compatible with - unlike
  `Flare.Api`'s endpoints, content negotiation buys nothing here.
- **It sees far higher throughput** - every ingested log/span/metric point,
  not occasional UI clicks - so allocation/CPU savings compound more here
  than anywhere in the HTTP surface.
- **It is a durable buffer, not a request/response.** ADR-0002's entire
  reason for existing is that a buffered-but-unflushed entry survives
  `Flare.Ingest` restarting. A version upgrade is a restart, so an entry
  written by the pre-upgrade binary can still be sitting un-acked in the
  stream (or a pattern-cluster bucket) when the post-upgrade binary starts
  reading - a wire-format change here has a real upgrade-compatibility
  question that a stateless HTTP endpoint never had to answer.
- **No `JsonElement`-shaped blocker.** Every member of `LogEvent`/
  `SpanRecord`/`SpanEvent`/`MetricPointRecord`/`ClusterRecord` - including
  every `IReadOnlyDictionary<string,string>` attribute bag and
  `IReadOnlyList<T>` collection - has a built-in MemoryPack formatter
  (confirmed by inspecting `MemoryPack.Core.dll`'s shipped formatter types
  directly rather than assuming from the README). Unlike `Flare.Api.Model.SavedView.State`'s
  opaque `JsonElement` (ADR-0015's one hand-written formatter), nothing here
  needed a custom `MemoryPackFormatter<T>`.

## Decision

**Full replacement, not content negotiation.** Every sink now writes
MemoryPack only - there is no second "ask for JSON" path to keep, because
nothing outside this process ever asks. This differs from ADR-0015's
additive stance deliberately: additive-forever only pays for itself when
there's an external caller that can't be migrated in lockstep, and there
isn't one here.

**A tagged envelope handles the upgrade seam instead of content
negotiation.** `Pipeline.RedisEventPayload.Encode<T>` writes a single
leading tag byte (`0x01`) followed by the MemoryPack encoding.
`RedisEventPayload.Decode<T>` takes the MemoryPack branch only when that
literal byte is present; anything else is handed to the caller's existing
`JsonSerializerContext` unmodified. A pre-migration JSON blob can never
start with `0x01` (JSON text starts with `{`/`[`/ASCII whitespace), so the
branch is exact, not a heuristic - every entry a pre-upgrade instance
already buffered still decodes correctly post-upgrade instead of failing
deserialization and eventually being dropped as a poison message by each
flush worker's existing `MaxDeliveryAttempts` reclaim logic. This is a
one-time upgrade seam, not a permanent second format: once every
pre-existing entry has drained through normal consumption, the JSON branch
is never exercised again until the next such upgrade.

**`RedisPatternClusterStore` needed a variant of the same idea, not the
same bytes.** Its Redis value is a `string` key, and its optimistic-
concurrency `Version` token is - by design, unchanged by this ADR - the
literal value read back (`Condition.StringEqual` against it on save). Raw
MemoryPack bytes are frequently invalid UTF-8 and would come back mangled
(lossy replacement characters) after a round trip through a C# `string`,
which would make every save spuriously conflict forever. The tagged bytes
are therefore base64-text-encoded before being written to the string key;
`Convert.FromBase64String` throwing `FormatException` (JSON's `{`/`"`/`:`/
`,` are never valid base64) is what distinguishes a pre-migration bucket
from a migrated one, playing the same role the leading tag byte plays for
the three Stream buffers.

**Every touched type gained `[MemoryPackable]` + `partial` directly** -
`LogEvent`, `SpanRecord`, `SpanEvent`, `ClusterRecord`, and
`MetricPointRecord`'s abstract base plus its three derived records. The
existing `JsonPolymorphic`/`JsonDerivedType` union on `MetricPointRecord`
is mirrored with `[MemoryPackUnion(tag, typeof(...))]` declarations on the
same base type - MemoryPack's union support needs no separate
`GenerateType` value (unlike a first guess against the API surface's
`GenerateType.Object`/`Collection`/`CircularReference`/`VersionTolerant`/
`NoGenerate` enum, which has no `Union` member; a plain `[MemoryPackable]`
plus the `[MemoryPackUnion]` tags is what the generator actually expects,
confirmed by building against the real 1.21.4 generator rather than
assumed).

**The `*JsonContext` classes stay, repurposed as the fallback contract.**
`LogEventJsonContext`/`SpanEventJsonContext`/`MetricEventJsonContext`/
`PatternClusterRecordJsonContext` are no longer what any sink writes, but
`RedisEventPayload.Decode`/`RedisPatternClusterStore.DecodeClusters` both
still need a `JsonTypeInfo<T>` for the fallback branch, so deleting them
would remove the only thing that makes the upgrade seam possible.

## Alternatives considered

- **Content-negotiated, additive (mirroring ADR-0015 exactly).** Rejected:
  there is no external caller to negotiate with - the "caller" asking for
  MemoryPack vs. JSON would always be this same process, making a
  negotiation mechanism pure overhead with no one on the other side of it.
- **No upgrade-compatibility handling; treat a format change as a breaking
  restart, same as any other schema change.** Considered, because
  `Flare.Ingest` deployments in practice restart both the writer and the
  reader together (they're the same binary). Rejected anyway: the entire
  premise of ADR-0002's Redis Streams buffer is that entries survive a
  restart, and a version upgrade is exactly the restart most likely to
  change the wire format - silently dropping whatever was buffered but not
  yet flushed at that exact moment would quietly violate the guarantee the
  buffer exists to provide, for a fix (the tag byte) cheap enough that
  skipping it wasn't worth the risk.
- **A schema-version field per record rather than a raw tag byte.**
  Rejected as solving a problem this boundary doesn't have: every writer is
  this same codebase, upgraded as one unit, so there's exactly one
  "current" format at a time needing exactly one bit of information
  ("is this the current format, or the one before it") - not an evolving
  multi-version schema needing to be told apart from many others.
  MemoryPack's own `VersionTolerant` mode targets that harder problem and
  was left unused here for the same reason.
- **Hash-based or counter-based version token for `RedisPatternClusterStore`
  instead of base64-wrapping the payload.** Would have required changing
  `IPatternClusterStore`'s `Version` contract (currently: an opaque token
  that happens to equal the stored bytes) and reworking the
  compare-and-swap to use something other than `Condition.StringEqual`
  against the literal value. Rejected: base64-wrapping the payload keeps
  the interface, the CAS mechanism, and `InMemoryPatternClusterStore`
  entirely unchanged, at the cost of ~33% size overhead on a low-volume,
  bounded-size path (one bucket's cluster list, not per-event).

## Consequences

- New dependency for `Flare.Ingest`: `MemoryPack` (`Flare.Ingest.csproj`),
  version shared with `Flare.Api` via the existing central
  `Directory.Packages.props` pin - no new version to track.
- New shared file `src/Flare.Ingest/Pipeline/RedisEventPayload.cs` - the
  one seam all three Stream sinks/flush workers go through; a future
  Redis-buffered signal should use it rather than reintroducing direct
  `JsonSerializer`/`MemoryPackSerializer` calls.
- `RedisEventPayload.Decode<T>` rewraps
  `MemoryPack.MemoryPackSerializationException` as `System.Text.Json.JsonException`
  on a malformed MemoryPack payload - the same fix ADR-0015's
  `ApiSerialization.ReadAsync` needed, applied here proactively (based on
  that precedent) rather than found again live: every existing
  `catch (JsonException ex)` in the three flush workers keeps working
  unmodified.
- `RedisPatternClusterStore`'s stored value is now base64 text wrapping a
  tagged MemoryPack payload rather than plain JSON - opaque to a human
  reading the raw Redis value directly (unlike the JSON it replaces,
  though `LogEventJsonContext`'s own remarks note that "eyeball it in
  Redis" motivation was specific to the plain PascalCase-property JSON
  contract, not claimed for this store).
- Full solution (`dotnet test Flare.slnx`) - all existing tests
  unchanged (the sinks/flush workers themselves are deliberately not
  unit-tested against a fake, per each project's own convention - see
  `src/Flare.Ingest/README.md`), plus 9 new tests in
  `src/Flare.Ingest.Tests/Pipeline/RedisEventPayloadTests.cs` (pure, no
  infra): a MemoryPack round trip for each of the four wrapped
  types/shapes (including the `MetricPointRecord` union dispatched through
  its abstract base, matching how the sink/flush worker actually call it),
  the legacy-JSON fallback decode path, and the malformed-payload rewrap
  into `JsonException` for both the MemoryPack and legacy-JSON branches.
  The three pre-existing `*JsonContextTests` files are kept and
  re-purposed as coverage for the fallback contract specifically (doc
  comments updated accordingly), not deleted.
- Verified live against a real `docker compose up` stack (ClickHouse +
  Redis + `Flare.Ingest`, no mocks), for all four types and both the
  normal path and the upgrade seam specifically:
  - A real OTLP log/trace/metric export landed a MemoryPack-tagged
    (`\x01`-prefixed) entry in `flare:logs`/`flare:spans`/`flare:metrics`
    (`redis-cli XRANGE`, raw bytes inspected directly) and a matching row
    in ClickHouse - including the metric gauge's union-tag byte (`0x00`)
    matching `MemoryPackUnion(0, typeof(GaugePointRecord))` exactly.
  - **The upgrade seam**: a hand-written legacy-JSON `LogEvent` blob was
    `XADD`-ed directly into `flare:logs` (simulating an entry a
    pre-upgrade instance had already buffered), and the running
    (MemoryPack-only) `ClickHouseFlushWorker` drained it into ClickHouse
    correctly, with no errors logged - confirming the fallback branch
    actually protects the guarantee ADR-0002 depends on, not just in
    `RedisEventPayloadTests`.
  - **`RedisPatternClusterStore`'s base64 variant of the same seam**: with
    `LogPattern__SharedStore=true`, a real log populated a bucket key as
    base64-wrapped tagged MemoryPack (decoded and confirmed directly); the
    bucket key was then overwritten with a hand-written legacy plain-JSON
    `ClusterRecord[]` array, and a second matching log both loaded that
    legacy bucket successfully (reusing its existing `PatternId` on the
    resulting ClickHouse row) and re-saved it in base64 format - the exact
    load-legacy/save-migrated transition the design depends on, confirmed
    against real Redis rather than assumed from the unit tests alone.

## Related documentation

- [`../investigations/memorypack-serialization-migration-scope.md`](../investigations/memorypack-serialization-migration-scope.md) -
  Finding 6, the scope assessment this ADR acts on.
- [`../investigations/memorypack-vs-json-benchmark.md`](../investigations/memorypack-vs-json-benchmark.md) -
  project-local BenchmarkDotNet numbers for this boundary specifically (Finding 1):
  MemoryPack wins on every axis (1.5-3.2x faster, up to 11x less allocation), decode
  gap widest - confirms the decision with Flare's actual `LogEvent` shape rather than
  the external SerializerBenchmark repo's generic payloads.
- [`0015-memorypack-content-negotiation-for-flare-api.md`](0015-memorypack-content-negotiation-for-flare-api.md) -
  the `Flare.Api` HTTP-surface decision this one deliberately diverges from
  (full replacement vs. additive) and why.
- [`0002-redis-streams-buffering.md`](0002-redis-streams-buffering.md) -
  why the buffer exists and survives a restart, the guarantee this ADR's
  tagged-fallback design protects across a version upgrade specifically.
