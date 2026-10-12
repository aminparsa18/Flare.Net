# ADR-0146: Terraform / OpenTofu provider

Status: Accepted (phase 1 implemented)

Date: 2026-10-07

## Context

Teams that manage infrastructure in Terraform or OpenTofu want Flare's alert rules, channels, SLOs and
dashboards in the same repo and review flow. The roadmap item assumed some endpoints were missing and that
a "config-as-code" item would settle naming. Checking the code first:

- **CRUD by GUID exists** for notification channels, alert rules, SLOs, dashboards, pipeline rules,
  maintenance windows and metric attribute rules (`src/Flare.Api/Endpoints/*Endpoints.cs`). The roadmap was
  wrong that maintenance windows and metric attribute rules lack it.
- **Gaps (closed by phase 1):** service accounts had only create plus access-token create/list (role and
  disable already work via `/api/users`), and ingest keys had no rename.
- **Secrets already round-trip safely.** Channel and alert reads mask credentials, and a masked value sent
  back means "unchanged" (`NotificationSecrets.Restore`). The provider can send what it holds without
  diffing secrets.
- **There is no config-as-code roadmap item.** The by-name convention already exists in
  `GET /api/alerts/export` / `POST /api/alerts/import`, which reference channels and SLOs by name.
- **The OpenAPI document is only mapped in Development** (`Program.cs`, `app.MapOpenApi()`).
- Go, Terraform and OpenTofu are not installed on the maintainer machine, so the provider cannot be built
  or acceptance-tested from this repo's current toolchain.

## Decision

- **Separate repo, Go, `terraform-plugin-framework`**, published to the Terraform and OpenTofu registries.
  Auth is a service-account access token (ADR-0082), so there is no session flow.
- **Hand-write resource schemas; don't generate them from OpenAPI.** The document isn't served in
  production, and the schemas that matter (write-only secrets, name-for-id resolution, computed ids) are
  exactly what a generator can't infer. Revisit only if the resource count outgrows hand-maintenance.
- **Names are the stable key; the provider resolves ids.** Resources reference each other by name
  (`channels = ["oncall-slack"]`) and the provider resolves to ids at apply time, matching the alert
  export format. This needs the server to enforce **unique names per resource type, case-insensitively**
  (Identity's `Username` and `Projects.Name` already do); where a type doesn't, adding that constraint is
  prerequisite server work, not provider work. Renames are an in-place update by id, never a recreate.
- **Secrets are `Sensitive` and write-only.** Webhook URLs, SMTP passwords and ingest-key secrets are never
  returned by the API. The provider keeps the configured value in state and does not diff it against the
  server; changing it forces an update. An ingest key's secret is only available from the create response
  and is stored in state as a sensitive computed attribute. Drift in a secret is undetectable and documented
  as such.
- **Compatibility via `/api/version` (ADR-0068).** The provider reads it at configure time and fails with a
  clear message when the server is older than the minimum for a resource it's asked to manage. Each
  resource documents the Flare version that introduced it.
- **Data sources** `flare_notification_channel` and `flare_service` (lookup by name) so rules can reference
  objects Terraform doesn't own.

## Phasing

1. **Server prerequisites in this repo** (done): `GET`/`DELETE /api/service-accounts/{id}` (delete is
   permanent, service accounts only), `PUT /api/ingest-keys/{id}/name`, and check-on-write name uniqueness
   (409, case-insensitive) for notification channels, alert rules and SLOs (`NameUniqueness`). No
   migration: these tables live in ClickHouse, which has no unique constraints, and checking on write
   means existing duplicates don't block anything, since an unchanged name is never rejected. Ingest-key
   names are unique among *active* keys so a rotated key can reuse its name. Dashboards are per-user, so
   their names stay non-unique. Pipeline rules, maintenance windows and metric attribute rules got the same check in phase 3 (a
   follow-up PR, ahead of their provider resources). Dashboards became unique per project in ADR-0147.
   The check is read-then-write, so two concurrent creates of one name can both succeed; acceptable for
   an admin-driven API, and the provider's single apply is sequential.
2. **Provider repo**: provider scaffold + `flare_notification_channel`, `flare_alert_rule`, `flare_slo`
   with acceptance tests against a throwaway `docker compose -p` stack.
3. Remaining resources (`flare_dashboard`, `flare_pipeline_rule`, `flare_maintenance_window`,
   `flare_metric_attribute_rule`, `flare_ingest_key`, `flare_service_account`) and registry publishing.

## Consequences

- Phase 1 is the only part doable from this repo today and is worth shipping alone: the CLI, import/export
  and any other client benefit from unique names.
- Phases 2-3 need a Go toolchain and a new repository, which is a decision for the maintainer.
- Existing duplicate names keep working but stay ambiguous for by-name lookup until renamed; the provider
  should error on an ambiguous name rather than pick one.
