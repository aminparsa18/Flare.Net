# ADR-0147: Dashboard names are unique per project

Status: Accepted

Date: 2026-10-07

## Context

ADR-0146 gives Terraform/OpenTofu resources a name as their stable key and left dashboards out because they are
per-user (ADR-0027): two users could each own a dashboard called "Overview". That blocks `flare_dashboard`, which
needs to find "its" dashboard by name for import and for adoption, while dashboard layout JSON is opaque to the
server (ADR-0023), so the provider cannot identify one by content either.

## Decision

Dashboard names are unique, case-insensitive, within one namespace: each project (ADR-0123) is its own namespace
and instance-wide dashboards share one. It is enforced on write with the same `NameUniqueness` check as
notification channels, alert rules, SLOs, pipeline rules and maintenance windows: a create or rename onto a taken
name returns 409, an update that keeps its name is never rejected, and existing duplicates keep working. Moving a
dashboard to another project is checked as a new name there. There is no migration; ClickHouse has no unique
constraints and the check is read-then-write, so two concurrent creates of one name can both succeed.

Ownership (ADR-0027) is unchanged: visibility stays global and mutation stays gated by owner/Admin, so uniqueness
is about naming, not privacy. The dashboard UI's Duplicate action appends a numeric suffix (`X (copy) 2`) rather
than hitting the 409.

## Consequences

- `flare_dashboard` can address a dashboard by `(project, name)`.
- Importing a Flare/Grafana export, creating from a template or pinning into a new dashboard with a name already
  in use now fails with a clear 409 instead of creating a lookalike.
- Two users who each named a dashboard identically before this change keep both; either can still be edited
  without renaming.
