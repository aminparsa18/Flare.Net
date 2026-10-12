# ADR-0160: Linking status incidents to components

Status: Accepted

Date: 2026-10-09

## Context

An incident (ADR-0159) belongs to a page as a whole. A visitor looking at five components cannot tell which one
the incident is about without reading it.

## Decision

- **An incident lists the components it affects**, by their `RefId` (the monitor or SLO id behind the
  component). Column `status_incidents.Components Array(UUID)` (migration 0074), empty by default: an incident
  need not be tied to a component.
- **Both write endpoints take an optional `components`.** Opening an incident sets the list. Posting an update
  replaces it when the field is present; absent leaves it unchanged and an empty list clears it. An id that is
  not a component of the page is a 400.
- **The public response carries display names**, not ids, in page order (`components` on each incident). An id
  whose component was later removed from the page is dropped from the public view and stays in storage.
- **It is a label only.** The component's computed state, and the banner, still come from the monitor or SLO
  (ADR-0158, ADR-0159).
- **The dashboard** adds a checkbox per component to the open-incident form and each update form, and the public
  page prints "Affected: ..." under the incident title.

## Alternatives considered

- **Store display names.** Rejected: renaming a component would orphan the link. `RefId` is the stable key.
- **A per-component status override on the incident** (mark a component Degraded while an incident is open).
  Rejected for now: it lets a human contradict the computed state, which ADR-0159 kept apart on purpose.

## Consequences

- Adding the column is additive (`ADD COLUMN IF NOT EXISTS`); existing incidents read as affecting nothing.
- Two components that share a `RefId` on one page are indistinguishable to an incident and are selected together.

## Not decided here

- Subscriptions, custom domains and branding, and a CLI command and Terraform resource for incidents.
