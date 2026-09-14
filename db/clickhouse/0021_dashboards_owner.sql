-- Dashboards schema, migration 0021.
--
-- Adds per-user ownership to `dashboards` (migration 0020) - the "Per-user dashboard
-- ownership" item docs-internal/planning/roadmap.md left open when Custom dashboards
-- shipped. See docs-internal/adr/0027-dashboard-ownership.md for the full design
-- rationale (why mutation-only, why a nullable owner, and why list/get stay unfiltered).
--
-- `OwnerUserId` is the creating `Flare.Identity.Users.User.Id`, captured once at create
-- time and never reassigned by an update (Flare.Api.Query.DashboardQueryService's
-- `UpdateAsync`/`DeleteAsync` both read the existing row via a C# `with` expression, which
-- carries this column forward untouched). `Nullable(UUID)`, not `UUID`, for two cases that
-- both mean "no owner to enforce against": a dashboard created while Flare's opt-in auth
-- (docs/auth.md) is disabled entirely (no `ClaimsPrincipal` to attribute it to), and every
-- dashboard that existed before this column did (backfilled implicitly - ClickHouse's
-- `ADD COLUMN ... DEFAULT NULL` needs no data migration, `FINAL`-read rows just report
-- `NULL` for it). `DashboardEndpoints`' ownership check treats a null owner as "anyone
-- Member-and-up may still mutate it" rather than "no one can" - see that file's own
-- remarks - so this migration alone never locks anyone out of an existing dashboard.
ALTER TABLE clickhousedb.dashboards
    ADD COLUMN IF NOT EXISTS OwnerUserId Nullable(UUID) DEFAULT NULL AFTER UpdatedAt;
