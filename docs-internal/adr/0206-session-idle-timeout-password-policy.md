# ADR-0206: Session idle timeout, password policy and admin sign-out

Status: Accepted

Date: 2026-10-12

## Context

Sessions had only an absolute 14-day lifetime, the single password rule was a hard-coded minimum of 8 characters
(duplicated in `AuthEndpoints` and `AdminProvisioner`), and an admin could revoke another user's sessions only as a
side effect of generating a password-reset link, which is only valid for local accounts.

## Decision

- **Idle timeout.** `Auth:SessionIdleTimeout` (unset by default, so behaviour is unchanged). `SessionAuthenticationHandler`
  compares `Sessions.LastSeenAt` with the timeout and deletes the session when it has been idle too long. `LastSeenAt` is
  already refreshed at most once a minute, so no schema change; the cost is that the timeout is only accurate to about a
  minute. The cookie keeps its absolute expiry, the idle check is server-side only.
- **Password policy.** `Auth:PasswordPolicy:{MinLength,RequireUppercase,RequireLowercase,RequireDigit,RequireSymbol}`, one
  `PasswordPolicy.Validate` used by bootstrap, set-password (invite/reset), change-password and `Identity:Admin:*`
  provisioning. Defaults reproduce the old behaviour (length 8 only). Login does not re-check it, so tightening the policy
  never locks out an existing account. It is configuration rather than a settings-table row: it needs no migration in
  either provider and is read by the same `AuthOptions` as the other session settings.
- **Admin sign-out.** `POST /api/users/{id}/sign-out` revokes every session of any account (local or SSO) without
  touching the password. Personal access tokens are unaffected. The self-service equivalent (`DELETE /api/auth/sessions`)
  already existed.

## Consequences

- The policy is not shown in the UI before submission; the API's 400 message names the missing requirements.
- Ingest key expiry is a separate decision: [ADR-0207](0207-ingest-key-expiry.md).
