# ADR-0027: Per-user dashboard ownership - mutation-gated, not visibility-gated

Status: Accepted

Date: 2026-09-14

## Context

ADR-0023 shipped dashboards as a global resource - visible and, subject to
role (`RequireMember`), mutable by every authenticated user, same as
`saved_views` and `alert_rules` always have been. The roadmap tracked
"Per-user dashboard ownership" as the one still-open item under Custom
dashboards, without pinning down what "ownership" should actually change:
Flare has no ownership model anywhere else in the app to follow as
precedent, so this needed deciding from scratch rather than mirrored from
an existing convention.

Two questions needed answering before writing any code:

1. Does ownership narrow *visibility* (a dashboard only its owner - and
   maybe people it's explicitly shared with - can see), or only
   *mutation* (everyone still sees everything, but only the owner/an
   Admin can change a given one)?
2. Auth is opt-in (`docs/auth.md`); dashboards created before this shipped
   have no creator to attribute. What happens to those, and what happens
   on an instance with auth disabled entirely?

## Decision

**Ownership gates mutation only. Visibility is unchanged - `GET
/api/dashboards` and `GET /api/dashboards/{id}` keep returning every
dashboard to every authenticated user, Viewer included, exactly as
before this ADR.** `PUT`/`DELETE /api/dashboards/{id}` additionally
require the caller to be the dashboard's own owner or an Admin, on top of
the existing `RequireMember` (Admin-or-Member) route policy. A Member who
didn't create a given dashboard now gets a 403 attempting to rename,
re-layout, or delete it - previously any Member could.

This was the narrower of the options considered specifically because nothing
in the roadmap's own "todo: global visibility" phrasing asked for private
dashboards, and a visibility change is a much bigger one (new query
filtering on every read path, a sharing UI, more to explain in the docs) for
a roadmap item that was really about *someone accidentally clobbering
someone else's dashboard*, not about hiding dashboards from each other. If
private-by-default dashboards are wanted later, this ADR's `OwnerUserId`
column is exactly the field such a follow-up would filter `ListAsync`/
`GetAsync` on - nothing here needs undoing to get there.

**`dashboards.OwnerUserId` is `Nullable(UUID)`, and null means "anyone
Member-and-up may still mutate it", not "no one can."** It's set once, at
create time, to the creating `User.Id` (`ClaimsPrincipal`'s
`ClaimTypes.NameIdentifier`, resolved the same way
`PersonalAccessTokenEndpoints.TryGetCurrentUserId` already does - and,
deliberately, duplicated rather than shared, matching every other small
per-endpoint-file helper in this codebase) and never reassigned by an
update. It's null for:

- every dashboard created before this migration (ClickHouse's `ADD COLUMN
  ... DEFAULT NULL` needs no backfill - a `FINAL`-read row for one of
  these just reports `NULL`), and
- any dashboard created while Flare's opt-in auth is disabled (no
  `ClaimsPrincipal` to attribute it to at all).

Both cases get the same treatment: `DashboardEndpoints.CanMutate` treats a
null owner as "unowned, anyone Member-and-up may mutate it" rather than
"owned by no one, so no one may." The alternative - locking every
pre-existing dashboard until an Admin manually reassigns it, or having the
first post-migration editor silently "claim" it - was rejected as
surprising with no real security upside: an instance's whole Member/Admin
set could already mutate every dashboard before this ADR, so a flag day
that suddenly can't is a regression with no attacker it stops (anyone in
that set who wanted to mutate a given dashboard, already could, and still
legitimately should be able to for one nobody's claimed).

## Consequences

- New ClickHouse migration (`0021_dashboards_owner.sql`, plus its cluster
  variant) - `ADD COLUMN IF NOT EXISTS OwnerUserId Nullable(UUID) DEFAULT
  NULL`, additive per this repo's migration convention, no backfill query
  needed.
- `Dashboard.OwnerUserId` is a new field on an already-`[MemoryPackable]`
  wire type, so `Dashboard.ts` (hand-written, not source-gen'd - see its
  own header comment) needed the field added by hand in the same
  declared-field order, using `writeNullableGuid`/`readNullableGuid` and
  bumping every `writeObjectHeader`/count-based version-compat branch from
  6 to 7 fields. `DashboardRequest` is untouched - ownership is
  server-assigned, never client-sent, same as `Id`/`CreatedAt`/`UpdatedAt`
  already are.
- `IDashboardQueryService.CreateAsync` gained an `ownerUserId` parameter;
  `UpdateAsync`/`DeleteAsync` didn't need to change at all -
  both already round-trip the existing row through a C# `with`
  expression, which carries `OwnerUserId` forward untouched since neither
  overrides it.
- The ownership check lives in `DashboardEndpoints`, not
  `DashboardQueryService` - consistent with this codebase's existing
  split (query services are plain ClickHouse CRUD with no authorization
  logic anywhere else either; `PersonalAccessTokenEndpoints` puts its own
  owner-or-Admin revoke check at the same layer). `HandleUpdateAsync`/
  `HandleDeleteAsync` now do one extra `GetAsync` before mutating, to have
  something to check `OwnerUserId` against - the same shape
  `PersonalAccessTokenEndpoints.HandleRevokeAsync` already uses
  (`FindAsync` then compare).
- Dashboard endpoint handlers became `internal` (from `private`) so
  `DashboardEndpointsTests` can exercise `CanMutate`'s owner/non-owner/
  Admin/unowned/auth-off matrix directly against fakes, the same
  `AuthEndpointsTests`-style "handlers made internal via
  `InternalsVisibleTo`, executed via `IResult.ExecuteAsync` against a real
  `DefaultHttpContext`, no ClickHouse involved" convention
  `AuthEndpointsTests` already established - `DashboardQueryService`
  itself stays untested per this repo's "ClickHouse-touching services are
  e2e-verified, not unit-faked" rule.
- Dashboard-mutating UI controls (rename/delete on the list table; edit/
  add-panel/manage-variables in the viewer) are now gated by
  `AuthState.canMutateDashboard(ownerUserId)`, not the coarser
  `AuthState.canMutate` alone - same "UI-only, API enforces it
  independently" caveat `canMutate` itself already carries.
  `PinToDashboardDialog`'s "pin into an existing dashboard" picker filters
  its list the same way, so a Member is never offered a target dashboard
  that its own pin-as-update would then 403 against.
- No new UI copy/i18n: this narrows *which* controls a given user sees,
  it doesn't add any new text to translate. The one user-facing doc
  change is a short note in `docs/how-to/build-custom-dashboards.md` (and
  its fr/ru/zh-CN translations) explaining the owner-or-Admin rule.
- Deliberately no "created by" display anywhere yet (table column, viewer
  header, etc.) - Flare has no existing "resolve a `User.Id` to a
  username for a non-Admin caller" endpoint to build it on, and adding
  one was out of scope for what the roadmap item asked for. A future pass
  wanting that can add such a lookup without touching anything here.

## Related documentation

- `docs-internal/adr/0023-custom-dashboards.md` - the dashboard CRUD/
  visibility baseline this ADR narrows.
- `docs/how-to/build-custom-dashboards.md` - user-facing walkthrough,
  updated alongside this ADR.
