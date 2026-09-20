# ADR-0029: Redis-backed cache in front of the log/metric query services

Status: Accepted

Date: 2026-09-20

## Context

`docs-internal/planning/roadmap.md`'s "Query result caching (Redis-backed)"
item flagged that Flare's Redis instance was, until now, only the ingest
buffer between `Flare.Ingest` and ClickHouse (ADR-0002) - nothing sat in
front of `LogQueryService` or `MetricQueryService`. Dashboard panels and
the Logs/Metrics Explorer pages poll their own query on a fixed interval
(auto-refresh, PR #253/#273's drag-to-zoom aside), and a saved search or a
custom dashboard re-runs the exact same request every time a viewer
(re)opens it. Every one of those repeats a full ClickHouse query - each
already execution-capped (`QueryOptions.CustomSettings`, see the "Query
safety" README section) but still real scan work - for a result that, for
anything but the most recent data, hasn't changed since the last poll.

## Decision

**A thin `ICacheProvider` seam (`Flare.Api/Caching/`), backed by Redis,
wrapping only `LogQueryService.SearchAsync`/`AggregateAsync` and
`MetricQueryService.QueryAsync`** - the three calls the roadmap item names
and the ones an auto-refreshing panel or a reopened saved search actually
repeats verbatim. Concretely:

- `RedisCacheProvider` implements `GetOrCreateAsync<T>` against the same
  `IConnectionMultiplexer` `Program.cs` already registers for live-tail and
  the Data Protection key ring - no new connection, no new container.
- `CachingLogQueryService`/`CachingMetricQueryService` decorate the real
  `LogQueryService`/`MetricQueryService` (both still registered directly in
  DI so the decorators can depend on the concrete class rather than risk
  wrapping themselves) and implement `ILogQueryService`/`IMetricQueryService`
  in their place. Every other member is a pass-through - pattern/attribute/
  value-distribution/active-service lookups, the SQL-query-row surface, and
  metric name/attribute-key discovery aren't the repeated-poll hot path
  this exists for.
- **Cache keys are a SHA-256 hash of the request's own MemoryPack encoding**
  (`QueryCacheKey`), not a hand-built string listing each `LogFilter`/
  `MetricFilter` field. Every Flare.Api request DTO is already
  `[MemoryPackable]` for the dashboard wire format (ADR-0016,
  `Json.ApiSerialization`), so this reuses an encoding that already exists
  and stays correct automatically as those DTOs gain fields, instead of a
  second hand-maintained key-shape that could silently drift from what the
  request actually carries.
- **Cached values are MemoryPack bytes, not JSON** - same reasoning: no
  second serializer format to maintain for a value this codebase already
  knows how to round-trip.
- **A cache-bypass guard for recent time ranges** (`CacheRecencyGuard`): a
  request whose `Filter.To` is null (open-ended, i.e. "now") or within
  `QueryCacheOptions.RecentWindow` (default 2 minutes) of the current time
  skips the cache entirely, both on read and write. Redis Streams (ADR-0002)
  buffers events before `ClickHouseFlushWorker` inserts them, so a window
  touching "now" can still gain rows for a short while after it's first
  read - caching that window risks a viewer being stuck seeing an
  undercount for the cache's full TTL instead of the query's own natural
  eventual-consistency lag.
- Tunable via a new `QueryCache` config section (`Enabled`, `Ttl` - default
  30 seconds, `RecentWindow`) bound the same way every other options class
  in this codebase is (`LiveTailOptions`, `AuthOptions`, etc.) -
  `Enabled: false` makes every decorator a pure pass-through with no Redis
  round trip, for anyone who'd rather not pay even a 30-second staleness
  window.

## Alternatives considered

- **An in-process `IMemoryCache` instead of Redis.** Rejected: ADR-0004
  already accepts that `Flare.Api` runs single-replica today, so an
  in-memory cache would work correctly right now - but the roadmap item
  explicitly asked for "Redis-backed," and a process-local cache is dead
  weight the moment that single-replica constraint is lifted (the same
  documented Postgres-migration follow-up ADR-0028 leans on), while Redis
  is already a hard dependency of this deployment and costs nothing extra
  to reuse here.
- **Caching inside each query service method directly**, rather than a
  decorator. Rejected: `LogQueryService`/`MetricQueryService` are the one
  seam in each project that actually touches ClickHouse (see their own
  class remarks) - mixing cache-key construction and recency logic into
  that class would blur "translates a request into SQL and reads it back"
  with "decides whether to skip that work," and would make the ClickHouse-
  querying code paths untestable-as-pure the way `LogFilterSqlBuilder`/
  `LogSearchQueryBuilder` etc. already are. A decorator keeps both classes
  exactly as they were and keeps the new caching logic (`CacheRecencyGuard`,
  `QueryCacheKey`) unit-testable on its own, with no ClickHouse/Redis I/O.
- **Caching `/api/logs/search`'s raw per-page rows keyed on filter+cursor.**
  Considered as part of the `SearchAsync` case above - accepted as in
  scope (the roadmap text calls out "saved-search reruns" by name, which
  is a `/search` call), not a separate decision.
- **A generic cache-key builder walking each request's fields by
  reflection**, to avoid a MemoryPack dependency in a new file. Rejected:
  MemoryPack is already the exact "encode this request/response shape"
  tool this codebase reaches for (ADR-0016) - reflecting over fields by
  hand would duplicate what `MemoryPackSerializer.Serialize` already does
  correctly, including nested records like `LogFilter.AttributeFilters`.

## Consequences

- A cached read for `/api/logs/search`, `/api/logs/aggregate`, or
  `/api/metrics/query` can lag reality by up to `QueryCacheOptions.Ttl`
  (default 30s) for any time range that ends more than
  `QueryCacheOptions.RecentWindow` (default 2 minutes) in the past. Ranges
  touching "now" are never cached, so live-tail-adjacent use (a panel
  showing the last few minutes) sees the same freshness it always did.
- No cache invalidation on write: nothing evicts a key early if new data
  lands mid-TTL for an already-cached-but-stale-adjacent range. Accepted -
  the recency guard already keeps genuinely fresh ranges out of the cache,
  and a 30-second worst case for an older, "settled" range is the explicit
  trade-off this ADR exists to make.
- No stampede protection: a concurrent miss on the same key can run the
  underlying ClickHouse query more than once. Accepted per
  `RedisCacheProvider`'s own remarks - the access pattern this exists for
  is one caller re-polling its own query, not many callers racing the same
  key, and the query itself is already execution-capped either way.
- Redis is now a load-bearing dependency for query *latency* on the hot
  read path, not just ingest durability/live-tail/session key persistence -
  but it was already a hard dependency of this deployment (ADR-0002), so
  this adds no new failure mode, only a new reason an existing one would
  be noticed.

## Related documentation

- `docs-internal/adr/0002-redis-streams-buffering.md` - the existing Redis
  dependency this reuses, and the reason recent ranges must bypass the
  cache.
- `docs-internal/adr/0016-memorypack-dashboard-typescript-adoption.md` -
  the MemoryPack wire format `QueryCacheKey`/`RedisCacheProvider` reuse for
  hashing and storage.
- `src/Flare.Api/README.md`'s "Query result caching" section - the
  user-facing shape of this decision, updated alongside this ADR.
