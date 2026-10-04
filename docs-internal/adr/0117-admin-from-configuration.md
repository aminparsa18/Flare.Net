# ADR-0117: Provision the admin account from configuration

Status: accepted

## Context

The first admin could only be created interactively through `/api/auth/bootstrap`, so headless installs
(compose, the `flare` CLI, Kubernetes) could not come up with a known login.

## Decision

`Identity:Admin:Username` plus `Password` or `PasswordFile` (a secret mount; one trailing newline is
trimmed, and the file wins over `Password`) are read by `AdminProvisioner` after the identity
migrations in `Flare.Api` startup. With no users in the database it creates that account as Admin.
Passwords shorter than 8 characters, or a half-configured pair, are skipped with a warning.

`Identity:Admin:Reconcile=true` additionally resets the named local account on every start to the
configured password, Admin role and enabled. Because users can't be deleted, this is all the
protection the account needs, so the UI is unchanged. SSO and service accounts are never reconciled.

## Consequences

No migration. Once any user exists the settings are inert unless `Reconcile` is on, so a leftover
password in config is harmless. With `Reconcile` on, the config password is the source of truth, so a
password changed in the UI reverts on the next restart.
