# ADR-0159: Status page incidents

Status: Accepted

Date: 2026-10-09

## Context

A status page (ADR-0158) shows computed health. When something breaks, visitors also want a person's words:
what is wrong, whether it is understood, and when it was fixed. Computed state cannot say that, and it
should not decide what is announced.

## Decision

- **An incident belongs to one page** and is written by an Admin. Table `status_incidents` (migration 0073,
  the same tombstone `ReplacingMergeTree` shape as `status_pages`) holds a title and the whole timeline as a
  JSON array of updates, oldest first. Posting an update inserts one new version of the row.
- **An update carries a status**: Investigating, Identified, Monitoring or Resolved. The incident's status is
  its latest update's, and it is resolved when that is Resolved. A resolved incident can be reopened by posting
  a later update with another status. There is no separate "open/closed" flag to disagree with the timeline.
- **Admin API** under the existing Admin group: `GET` and `POST /api/status-pages/{id}/incidents`,
  `POST .../incidents/{incidentId}/updates` and `DELETE .../incidents/{incidentId}`. A page that does not
  exist, or an incident on another page, is a 404. An incident takes at most 100 updates.
- **The public response gains `incidents`**: open incidents (oldest first), then those resolved within the last
  14 days (newest first), each with its updates newest first. It carries no ids. It rides the same 30-second
  cache as the rest of the page, so an update can take that long to appear.
- **Incidents do not change the computed state.** The overall banner still comes from monitors and SLOs; the
  incident is the human explanation shown next to it.
- **The dashboard** adds an Incidents dialog per page in Settings > Status pages and renders the incidents
  above the components on `/status/{slug}`.

## Alternatives considered

- **One row per update.** Rejected: it needs a join or a second read per page and the volume is tiny (a
  handful of updates per incident).
- **Deriving incidents from alert events.** Rejected: alerts are internal and noisy, and publishing is a
  deliberate act (ADR-0158).
- **A separate resolved flag.** Rejected: it can disagree with the timeline.

## Consequences

- Update text is shown to anyone who can open the page and is not sanitised beyond being rendered as text by
  the dashboard; admins should not paste secrets.
- Deleting a page leaves its incident rows behind; they are unreachable because every read is by page id.

## Not decided here

- Subscriptions (email or webhook on a new incident), a CLI command and a Terraform resource for incidents,
  and linking an incident to the components it affects.
