# ADR-0162: Status page visitor subscriptions

Status: Accepted

Date: 2026-10-09

## Context

ADR-0161 lets an admin point a status page at notification channels they own. Visitors have no way to ask to
be told themselves. Email is the one channel a stranger can safely supply: a visitor-supplied webhook URL would
make Flare POST to an address of their choosing (SSRF), and Telegram or Slack need accounts Flare does not own.

## Decision

- **Email only, double opt-in.** `POST /api/public/status/{slug}/subscribe` takes an address and mails a
  confirmation link; nothing else is ever sent until it is followed. Table `status_subscribers` (migration
  0076, the usual tombstone `ReplacingMergeTree`) holds page, lower-cased address and a `Verified` flag. The
  row id is derived from page and address, so subscribing twice is one row.
- **The form never reveals who is subscribed.** A new address, an already verified one and one still inside
  the resend cooldown all get the same `202`. An unverified address is mailed again at most every 10 minutes,
  so the form cannot be used to mail-bomb someone. A page keeps at most 2,000 subscribers (409 beyond that).
  The endpoints share a per-caller-address limit of 30 requests per hour. Behind a reverse proxy that address
  is the proxy's, so this is only a backstop; the cooldown and cap are the real bounds.
- **Links are signed, not stored.** Confirm and unsubscribe links carry a Data Protection token (the same
  mechanism as ack links, ADR-0127) bound to page and address, with separate keys per purpose so one cannot be
  used as the other. A confirm link lives 3 days. An unsubscribe link, which sits in every email, lives 10
  years. Redeeming a link is a POST from a button page, so a mail scanner that fetches the URL changes
  nothing. Unsubscribing is idempotent. The unsubscribe endpoint also accepts a bare POST with the token in
  the URL, which is what `List-Unsubscribe-Post: List-Unsubscribe=One-Click` mail clients send.
- **Delivery** is one email per verified subscriber per incident open or update, each with its own
  unsubscribe link and `List-Unsubscribe` headers, over one SMTP connection using the existing `Email__*`
  settings. It runs in `StatusIncidentNotifier` next to the channel fan-out of ADR-0161 and is equally best
  effort: it happens after the incident is saved and failures are logged.
- **Availability.** Subscribing needs `Email__Host`, `Email__From` and `Alerting__PublicUrl` (the base of
  every link). Without them the public page reports `subscribable: false`, so the dashboard hides the form,
  and the subscribe endpoint answers 503.
- **Admin** can list a page's subscribers and remove one: `GET /api/status-pages/{id}/subscribers` and
  `DELETE .../subscribers/{subscriberId}`.
- **The dashboard** adds a sign-up form to `/status/{slug}` and the `/subscribe/confirm` and
  `/subscribe/unsubscribe` pages the emails link to.

## Alternatives considered

- **Visitor-supplied webhooks.** Rejected: SSRF, and each would need ownership verification.
- **Storing a random token per subscriber.** Rejected: signed tokens need no extra column and cannot be
  guessed; revoking everything is a purpose-suffix bump.
- **Single opt-in.** Rejected: anyone could subscribe someone else's address.

## Consequences

- Every incident update emails every verified subscriber; there is no per-component filter or digest.
- Unverified rows are never purged (the cap bounds them); an admin can remove them.
- Email bodies are plain text.

## Not decided here

- Per-component subscriptions, digests, a CLI or Terraform view of subscribers, and SMS or RSS.
