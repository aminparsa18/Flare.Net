# ADR-0089: Dashboard tags and per-user pins

Status: Accepted

Date: 2026-10-03

## Context

Dashboards had no organisation beyond a name sort and the per-browser home dashboard
(`home-preference.ts`). Past a few dozen dashboards the list page is hard to scan.

## Decision

**Tags are part of the dashboard; pins are part of the user.**

- `dashboards.Tags Array(String)` (migration 0043, cluster variant included). It rides the
  dashboard's existing versioned row, so tag edits go through the same update path and
  `LatestVersionSql` reads as everything else. `Dashboard.Tags` and `DashboardRequest.Tags`
  are appended last on the MemoryPack types; the hand-written TS companions follow.
- `DashboardTags.Normalize` trims, lowercases, de-duplicates and drops blanks, so "Prod" and
  "prod" are one chip. Limits: 10 tags, 32 characters each, enforced by the create/update
  endpoints (400).
- `DashboardRequest.Tags == null` on update means "leave tags unchanged", not "clear". Callers
  that don't know about tags (the viewer's layout save, "pin panel to dashboard") therefore
  can't wipe them; `[]` clears.
- Pins are an Identity SQLite table (`DashboardPins`, migration 0022): `(UserId, DashboardId,
  PinnedAt)`. No foreign key, since dashboards live in ClickHouse; a pin for a deleted
  dashboard is inert. Endpoints: `GET /api/dashboards/pins`, `PUT|DELETE
  /api/dashboards/{id}/pin`. They need only authentication, not `RequireMember`, because a pin
  is a personal ordering preference and changes nothing for anyone else. With auth disabled the
  owner is `Guid.Empty`, so pins are shared.
- The pins endpoints use plain JSON, not MemoryPack: the payload is a bare id list.
- The list page filters client-side (search over name/description/tags, AND across selected
  tag chips) and sorts pinned dashboards first, most recently pinned on top. The list is
  already loaded in full, so there is no server-side filter.

## Consequences

- The roadmap item also named the command palette. Flare has no command palette today, so
  that part is not built; the "Pin panel to dashboard" picker does not yet float pins either.
- Tags are plain labels: no colours, no tag management page, no rename-a-tag-everywhere.
- Pins don't follow a user across auth providers (a new `UserId` starts with none).
