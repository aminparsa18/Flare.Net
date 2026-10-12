# ADR-0164: Status page subscriber preferences link

Status: Accepted

Date: 2026-10-09

## Context

ADR-0163 lets a visitor pick components when subscribing, but a verified address ignores a new request, because
otherwise anyone who knows an address could narrow that person's alerts from the public form. The only way to
change a selection was to unsubscribe and subscribe again.

## Decision

- **A third signed-link purpose.** `Flare.StatusSubscription.Preferences.v1`, with its own key like the confirm
  and unsubscribe links (ADR-0162), bound to page and address, valid 10 years because it sits in every email.
- **The link proves the address**, so it may change what that address receives. `GET
  /api/public/status/subscriptions/preferences?token=` describes the subscription (page title, address, the
  page's components as key and name, and the keys currently chosen) and changes nothing. `POST` on the same
  route takes the token and the new `components` and saves them, with the normalisation of ADR-0163 (none or all
  means every component; an unknown key is a 400). It shares the subscription rate-limit policy.
- **Verified subscribers only.** An unconfirmed or removed address answers 404; nothing can be changed before
  the address is confirmed.
- **In the email.** Incident emails add a "Choose which components you hear about" line when the page has more
  than one component. The dashboard serves it at `/subscribe/preferences`, a button page like the others.

## Alternatives considered

- **Re-sending a confirmation to change the selection.** Rejected: slower for the subscriber and one more email
  for the same proof.
- **Letting the public form edit a verified address.** Rejected for the reason in ADR-0163.

## Consequences

- A leaked preferences link lets its holder change that one subscription's components, but not unsubscribe it
  or read anything beyond the page's public component names.
- Bumping the purpose suffix invalidates every outstanding preferences link.

## Not decided here

- Digests, and a name-resolving admin view of subscribers' selections.
