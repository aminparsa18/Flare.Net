# ADR-0163: Per-component status page subscriptions

Status: Accepted

Date: 2026-10-09

## Context

ADR-0162 emails every verified subscriber about every incident on a page. A page with a dozen components
(API, billing, EU region, ...) makes that noisy for a visitor who only depends on one of them. Incidents
already say which components they affect (ADR-0160).

## Decision

- **Opaque component keys.** The public page now gives each component a `key`: a hash of the page id and the
  component's monitor or SLO id, shaped as a GUID. It is stable, per page and not reversible, so the public
  response still carries no internal id (ADR-0158).
- **Stored on the subscriber.** Migration 0077 adds `status_subscribers.Components Array(UUID)`. Empty, the
  default and every existing row, means every component.
- **The form sends keys.** `POST /api/public/status/{slug}/subscribe` takes an optional `components` list. An
  unknown key is a 400. Picking none, or every component, is stored as empty so a component added to the page
  later is not silently excluded.
- **Matching.** A subscriber is mailed about an incident when its list is empty, when the incident names no
  component, or when the two overlap. An incident that names no component reaches everyone because nobody can
  tell whether it concerns them. The check runs per update, so an update that adds a component to an incident
  starts reaching that component's subscribers.
- **Changing the selection** is only possible while an address is unconfirmed (a new request within the
  resend cooldown changes nothing, as before). A verified address ignores a new request: otherwise anyone who
  knows an address could narrow that person's alerts from the public form. To change it, unsubscribe and
  subscribe again.
- **Admin view.** The subscriber list returns the keys and `flare status-pages subscribers list` shows `all`
  or a count.

## Alternatives considered

- **Keying by component name.** Rejected: names are editable and need not be unique.
- **Exposing the monitor or SLO id.** Rejected: it would leak internal ids on an unauthenticated endpoint.
- **A preference link in every email.** Deferred: it needs a third signed-link purpose and a page. It is the
  natural way to edit a verified subscription later.

## Consequences

- Removing a component from a page leaves its key in old subscribers' lists, where it matches nothing. A subscriber
  who only picked removed components stops receiving incidents that name components, but still gets unscoped ones.
- Admin screens show keys, not names, for a subscriber's selection.

## Not decided here

- Editing a verified subscription's components, digests, and a name-resolving admin view.
