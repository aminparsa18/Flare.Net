# ADR-0028: Per-token request-frequency limiting for personal access tokens

Status: Accepted

Date: 2026-09-15

## Context

ADR-0019 shipped personal access tokens (PATs) as a bearer credential for
`Flare.Api`'s query endpoints. `docs-internal/planning/roadmap.md`'s "Rate
limiting" item flagged the gap this left open: every ClickHouse query
already sets execution caps (`max_execution_time`, `max_rows_to_read`,
etc. via `QueryOptions.CustomSettings`) that bound the *cost* of one
request, but nothing bounds how *often* a given token can call at all. A
leaked token, or a script with a bug in its retry/polling loop, can hammer
the API without limit.

## Decision

**A named ASP.NET Core rate-limiting policy (`Microsoft.AspNetCore.RateLimiting`,
part of the shared framework since .NET 7 - no new package reference),
partitioned by personal-access-token id, applied once to the whole
`authenticatedRoutes` group in `Program.cs`** - every route that currently
accepts a PAT (`/api/logs/*`, spans, metrics, services, exceptions, saved
views, dashboards, ingestion/pipeline/indexing config, host stats, and the
PAT self-service endpoints themselves) picks the limit up for free, rather
than cherry-picking a subset. `memberRoutes`/`adminRoutes` (alert rules,
notification channels, ingest keys, user/settings admin - mutating,
low-QPS by nature) stay out of scope.

Concretely:

- `SessionAuthenticationHandler.BuildTicket` now adds a
  `FlareClaimTypes.PersonalAccessTokenId` claim, but only on the PAT path
  - never on a cookie-session ticket. This is the only way to tell the two
  apart; before this change nothing on the resulting `ClaimsPrincipal`
  distinguished them.
- The policy's partition-key factory reads that claim straight off
  `HttpContext.User`: absent (a cookie session) → `RateLimitPartition.GetNoLimiter(...)`,
  never limited; present → a `FixedWindowRateLimiter` keyed on the token
  id, `AuthOptions.PatRateLimitPermitLimit` (default 120) requests per
  `AuthOptions.PatRateLimitWindow` (default 1 minute).
- Rejection returns `429` with a `Retry-After` header computed from the
  rejected lease's `MetadataName.RetryAfter` metadata - the same
  `Results.Problem(..., statusCode: 429)` + `Retry-After` shape
  `AuthEndpoints.HandleLoginAsync` already uses for login-lockout
  rejections, for one consistent 429 response shape across the app.

**In-memory state, not a new SQLite-backed store mirroring
`ILoginAttemptStore`.** That was the first design considered and rejected:
ADR-0004 already establishes, as an accepted consequence, that
`Flare.Api` can only run as a single replica (SQLite can't safely be
written by multiple processes sharing a volume) - so there is no
cross-process state for an in-memory limiter to fail to coordinate here,
and the alternative would add a disk write to every single authenticated
API request for no correctness benefit `ILoginAttemptStore` actually
needs (that store's persistence matters because a lockout must survive a
restart; losing accumulated request counts across a `Flare.Api` restart
here is an unnoticeable, harmless reset).

## Alternatives considered

- **A SQLite-backed fixed-window counter, structurally mirroring
  `SqliteLoginAttemptStore`.** Rejected per the reasoning above - solves a
  multi-replica problem `Flare.Api` doesn't have, at the cost of a write
  amplifying every request and a new migration/table/interface/store this
  codebase doesn't otherwise need. If ADR-0004's single-replica constraint
  is ever lifted (its own documented Postgres-migration follow-up), this
  limiter's state moves with that migration the same way every other
  identity table would - nothing here is a special case blocking that
  path.
- **Scoping the policy to just `/api/logs/search`/`/aggregate`** (the two
  endpoints the roadmap bullet named explicitly). Rejected: the roadmap's
  actual framing is "nothing bounds request frequency per token," not
  "per query endpoint" - narrowing to two routes would leave every other
  PAT-usable endpoint (metrics, spans, dashboards, ingestion config, etc.)
  with the identical unbounded-frequency gap this ADR exists to close,
  for the sake of avoiding one filter placement instead of thirteen.
- **A custom `IEndpointFilter`** doing the same partition-and-count logic
  by hand. Rejected once the built-in middleware's `AddPolicy`/
  `RequireRateLimiting` was confirmed to already support a per-request
  partition-key factory with full `HttpContext` access (including
  `RequestServices`, for pulling live `AuthOptions`) - reimplementing that
  is strictly more code for the same behavior.

## Consequences

- `AuthOptions` gained `PatRateLimitPermitLimit`/`PatRateLimitWindow`,
  bound from the existing `Auth` config section - no new section.
- Rate-limit state resets on every `Flare.Api` restart/redeploy. Accepted:
  the limit exists to bound a runaway or leaked token's steady-state
  request rate, not to enforce a hard lifetime quota, so losing counts
  across a restart doesn't undermine its purpose.
- Partitioning on the PAT id is safe from the cardinality-DoS risk the
  ASP.NET Core docs call out for partitioning on raw user input (e.g. IP/
  header values): the partition key is only ever set *after*
  `SessionAuthenticationHandler` has validated the token against
  `IPersonalAccessTokenStore`, so an unauthenticated caller can't grow the
  partition set by varying an unauthenticated request.
- Cookie/session (dashboard SPA) traffic is completely unaffected,
  regardless of how many endpoints the policy covers - `RateLimitPartition.GetNoLimiter(...)`
  is a true no-op for any request without the PAT claim.

## Related documentation

- `docs-internal/adr/0019-personal-access-tokens.md` - the PAT credential
  this limits.
- `docs-internal/adr/0004-embedded-sqlite-for-identity.md` - the
  single-replica constraint this decision leans on.
- `docs/reference/authentication-config.md` / `docs/explanation/authentication-model.md`
  - user-facing docs, updated alongside this ADR.
