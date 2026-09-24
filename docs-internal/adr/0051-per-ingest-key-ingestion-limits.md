# ADR-0051: Per-ingest-key ingestion limits

Status: Accepted

Date: 2026-09-24

## Context

`Flare.Ingest` authenticates OTLP exporters with ingest API keys, but it has
never capped how much a key can send. One noisy app, or a misconfigured
debug-level logger, can flood Redis Streams and ClickHouse for every other
app on the same instance. An admin had no lever short of revoking the key,
which throws the data away.

The dashboard also had no page for ingest keys. Only the terminal's
`apikey create` command and the CLI used the endpoints.

Prior art: SigNoz added per-ingestion-key limits
([signoz#6430](https://github.com/SigNoz/signoz/commit/504bc0d541210c6fbe9e0a6509c10febf134a217)).

## Decision

**Optional per-key caps on events and bytes, per UTC minute and per UTC day,
enforced at the OTLP receiver against Redis counters.**

- **Shape.** Four independently optional caps (`MaxEventsPerMinute`,
  `MaxBytesPerMinute`, `MaxEventsPerDay`, `MaxBytesPerDay`) plus a separate
  `LimitsEnabled` toggle, stored as new columns on the SQLite
  `IngestApiKeys` table (Identity migration `0016`). The toggle is separate
  so an admin can switch enforcement off without losing the numbers. An
  "event" is one log record, span, or metric data point, the same unit the
  Ingestion page counts.
- **Counters live in Redis, not in-process.** Unlike PAT rate limiting
  ([ADR-0028](0028-personal-access-token-rate-limiting.md)), which is
  in-memory because `Flare.Api` is single-replica
  ([ADR-0004](0004-embedded-sqlite-for-identity.md)), ingest does run as
  several replicas (`docker-compose.cluster.yml`). A per-process counter
  would let a key through at N times its cap. `Flare.Api` also has to read
  the same numbers to show usage. Redis is already on this hot path for the
  sinks and the Ingestion page stats, so it adds no infrastructure. Key
  naming lives once in `Flare.Identity`'s `IngestApiKeyUsageKeys`, since
  both processes already reference that assembly.
- **Fixed windows.** One Redis hash per key per UTC minute and one per UTC
  day, `HINCRBY` to write, `HMGET` to read. "Per day" means the UTC calendar
  day, which is what an admin reading a daily cap expects.
- **Enforced in `IngestApiKeyValidationMiddleware`**, the one place that
  already resolves which key sent a request, for gRPC and HTTP alike. Only a
  key with enforced limits pays the extra Redis read. Every SQLite-backed
  key has its usage recorded, so the dashboard can show it: the receivers
  report accepted counts into a per-request `IngestKeyUsageFeature`, and the
  middleware writes the total to Redis once the handler returns.
- **A soft limit.** A request is admitted while usage so far is below every
  cap, and its full count is added afterwards. The record count isn't known
  until the body is parsed. A window can overshoot by the requests already
  in flight when it crossed the cap. Checking exactly would mean parsing
  every body in the middleware, or reserving capacity and refunding it,
  which is a lot of machinery for a fairness guard.
- **Rejection is the OTLP-spec throttling response, so exporters retry
  instead of dropping data.** OTLP/HTTP gets `429` with `Retry-After` and a
  protobuf or JSON `google.rpc.Status` body, which the spec requires for any
  4xx. OTLP/gRPC gets a trailers-only `RESOURCE_EXHAUSTED` carrying a
  `RetryInfo` detail. The spec only treats `RESOURCE_EXHAUSTED` as retryable
  when that detail is present. `Retry-After` is the time to the end of the
  exceeded window, and a reached daily cap wins over a per-minute one. The
  `google.rpc` protos are vendored like the OTLP ones
  (`src/Flare.Ingest/Protos/VENDORED.md`). Each rejection is also recorded
  on the Ingestion page as `ingest-key-limit:<key name>`.
- **Fail open on a Redis read error.** Redis being down already fails the
  export at the sink, so rejecting here as well gains nothing. A limit is a
  fairness guard, not a security boundary.
- **Scope.** Limits only apply while `Auth:IngestKeyRequired=true`.
  Otherwise an exporter could just omit its key. They never apply to
  `Auth:StaticIngestApiKey`, which has no row to hang limits on. A change
  reaches ingest within `IngestApiKeyCache`'s 30-second refresh, the same as
  a revocation.
- **Admin surface.** `PUT /api/ingest-keys/{id}/limits`, usage folded into
  `GET /api/ingest-keys`, and a new dashboard Ingest Keys page (create,
  revoke, edit limits, live usage) under the `⋯` menu.

## Alternatives considered

- **ASP.NET Core's built-in rate limiter, as for PATs.** It's in-process
  only, so it's wrong for multi-replica ingest. It also counts requests,
  not the events or bytes inside them, and nothing outside the process can
  read it back for display.
- **Sliding windows or a token bucket in Redis (Lua).** They're smoother at
  window edges but cost a script per request. They also make "usage today"
  harder to show and explain. Fixed UTC windows match what the dashboard
  displays.
- **A bare 429 or `UNAVAILABLE` for gRPC.** Without `RetryInfo`,
  `RESOURCE_EXHAUSTED` is non-retryable per spec, so exporters would drop
  the batch. `UNAVAILABLE` is retryable but claims the server is overloaded,
  which is misleading for a quota.
- **Record usage in the receivers directly.** That puts Redis writes and key
  awareness in six endpoint classes instead of one middleware.

## Consequences

- An admin can cap one noisy source without revoking it, and exporters back
  off instead of losing data (subject to their own retry and queue limits).
- One extra Redis round trip per accepted request from a SQLite-backed key
  (the usage write), and a second one before the handler for keys with
  enforced limits.
- Enforcement is approximate at the edges: a soft overshoot within a window,
  and up to 30 seconds before a changed limit applies.
- Usage is current-window only, not a history. The Ingestion page remains
  the place for trends.
- The CLI still only creates keys. Limits are dashboard and API only.
