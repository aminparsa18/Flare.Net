# ADR-0115: Bulk invite

Status: accepted

## Context

Onboarding a team meant one invite dialog per person (ADR-0112, ADR-0114).

## Decision

`POST /api/users/invite/bulk` takes `{ usernames, role }` (max 100, trimmed, case-insensitively
de-duplicated) and returns a per-name outcome: `Created` (with its one-time token and whether it
was emailed), `Exists`, or `Invalid`. It is not all-or-nothing, so one taken username doesn't block
the rest. Each created user gets the same 3-day invite token and optional email as a single invite.
The dashboard's invite box accepts several names (newline/comma/space separated); more than one
routes to this endpoint and opens a results dialog with "Copy all links".

## Consequences

Usernames containing spaces can't be bulk-entered. Tokens for the whole batch are returned in one
response, shown once.
