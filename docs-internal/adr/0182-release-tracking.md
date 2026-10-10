# ADR-0182: Release tracking

Status: Accepted

Date: 2026-10-10

## Context

Flare can say which version a span came from (`service.version`), compare two versions on demand
([ADR-0107](0107-deploy-version-comparison.md)) and flag a resolved error group that returns in a version it hadn't been
seen in ([ADR-0121](0121-error-issue-lifecycle.md)). What it cannot do is record that a version was *deployed*, with its
commit and time, or answer "which errors did this release introduce?" without picking two versions by hand.

## Decision

- **A release is a marker, not telemetry.** One `releases` table (migration 0080 and the cluster variant), a
  `ReplacingMergeTree(UpdatedAt)` with an `IsDeleted` tombstone read through `LatestVersionSql` (ADR-0009, ADR-0074),
  the same shape as `error_issues`. `Id` is a SHA-256 prefix of service and version, so marking the same pair again
  updates it.
- **API:** `GET /api/releases[?service=]`, `GET /api/releases/errors?service=&version=` (any authenticated user),
  `PUT` and `DELETE /api/releases` (Member/Admin; a deploy step calls them with a personal access token, ADR-0019).
  Project scope (ADR-0123) applies by service name. Fields: service, version, commit, url (http/https only), notes,
  `deployedAt` (defaults to now, at most a day ahead).
- **"New errors" are derived at read time.** An exception group (`exception.type` + `exception.message`, the Errors page
  grouping) is introduced by the version of its earliest `exception` span event inside a window that starts 30 days
  before the release's deploy time (`argMin` over the events). Nothing is stored per event or per group, so a late or
  repeated marker changes nothing in the data.
- **Counts only for one service.** The list carries `newErrorCount` only when scoped with `?service=`, because it scans
  `spans`; the dashboard picks a service first. The per-release error list is fetched when a row is expanded.
- **Regressed is not rebuilt.** ADR-0121 already derives it from `service.version` and works without markers; the
  Releases page links to the Errors page rather than duplicating the state.
- **Surfaces:** a Releases page (`/releases`, in the menu), `flare releases list/mark/delete`, and
  `docs/how-to/track-releases.md`.

## Consequences

- A group that was silent for more than 30 days before a deploy and then returned reads as introduced by that release.
- Telemetry without `service.version` belongs to no release, and a marker whose version never appears shows no errors.
- Attribution uses the earliest event's version, so a group that appears first under an older version that is
  still running during a rolling deploy is credited to the older version.
- The count query scans a service's exception events for the window on each load; the query caps from
  `Query__*` bound it.
- Deploy markers are not drawn on charts, and no alert fires on a bad release. Both are open on the roadmap.
