# ADR-0114: Emailed invites

Status: accepted

## Context

ADR-0112 left invites copy-and-paste only, and ADR-0113 added SMTP-backed mail for password resets.

## Decision

`POST /api/users/invite` also emails the set-password link when SMTP and `Alerting:PublicUrl`
are configured and the new username is an email address. The response still returns the raw
token (so the admin can copy it) plus `emailSent`. A failed send does not fail the invite.

## Consequences

Admin reset links stay copy-only. Bulk invite is still open.
