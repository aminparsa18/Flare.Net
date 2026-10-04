# ADR-0110: Server-side per-user UI preferences

Status: Accepted

Date: 2026-10-04

## Context

Settings > Appearance (nav layout, density, text size, content width, ...) lived only in
`localStorage`, so every browser and device started from defaults.

## Decision

- Identity SQLite table `UserPreferences (UserId, Key, Value, UpdatedAt)`, migration 0026. `Value`
  is an opaque JSON object; the dashboard owns its shape and validates every field on read, so a
  new preference needs no migration.
- `GET|PUT|DELETE /api/me/preferences/{key}`. Authentication only, not `RequireMember`: it is
  self-service and affects nobody else (same reasoning as pins, ADR-0089). `key` is allow-listed
  (`appearance` today) so the table can't become arbitrary storage; the body must be a JSON object
  of at most 4096 characters. `GET` returns 204 when nothing is stored. With auth disabled the
  owner is `Guid.Empty`, so preferences are shared.
- `localStorage` stays as the pre-paint cache: `app.html` still replays it before first paint, so
  there is no flash. After the app is ready the dashboard fetches the server copy once per page
  load; if one exists it wins and overwrites the local cache. If none exists, the browser's
  non-default prefs are pushed up, so existing settings migrate instead of being lost.
- Changes are pushed with a 500 ms debounce, best-effort. A failed or unavailable endpoint leaves
  local prefs authoritative.

## Consequences

- Last write wins; two browsers editing at once can overwrite each other. Acceptable for UI prefs.
- A user's first browser after upgrade seeds the server copy; a second browser with different
  local prefs adopts the server's on next load.
- Allow-listed keys: `appearance`, `regional`, `keyboard` (only the shortcut bindings that differ from the defaults: command palette, next/previous log event; rebinding rejects duplicates) and `explorer` (landing page, log table layout, live-tail
  defaults (open live, auto-scroll, buffer), chart bucket interval, facet sidebar default; read when an explorer's state is constructed). Theme joins the `appearance` document (mode-watcher
  still owns the pre-paint class; the root layout feeds its choice into the synced prefs and applies
  the server's choice with `setMode`). The `regional` document carries language, display time zone,
  time format, date order, first day of week and each explorer's default time range.
- Changing language reloads the page and the server copy would win on reload, so the new language is
  pushed before `setLocale` runs.
- Default time ranges are read when an explorer's state is constructed, so a server value fetched in
  a browser that has never seen it applies from the next page load.
