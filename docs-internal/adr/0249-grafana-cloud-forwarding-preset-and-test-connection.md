# ADR-0249: Grafana Cloud forwarding preset and test connection

Status: Accepted

Date: 2026-10-18

## Context

Forwarding to Grafana Cloud (ADR-0155, ADR-0157) worked, but only if you hand-built the gateway URL
(`https://otlp-gateway-<zone>.grafana.net/otlp`) and base64-encoded `instance:token` into an `Authorization: Basic`
header. A typo only showed up later as a failed counter in the status table.

## Decision

- **The preset is a dashboard form helper, not a new target kind.** Choosing Grafana Cloud when creating a target asks for
  region, instance ID and access policy token; `$lib/telemetry-export/grafana-cloud.ts` derives the endpoint and the Basic
  header, and the saved row is an ordinary forwarding target. No API field, migration, CLI or Terraform change, and
  Flare.Ingest and the audit snapshots see nothing new. The region accepts `prod-us-central-0`, `us-central-0` or a pasted
  gateway URL.
- **`POST /api/forwarding/test` probes a destination without saving it.** Admin-only, it POSTs an empty protobuf
  `ExportLogsServiceRequest` (a valid export of nothing) to `{endpoint}/v1/logs` with the submitted headers and returns
  `{ success, statusCode, url, error }`. A masked header value is resolved against the saved target named by `id`
  (`NotificationSecrets` rules), so testing an existing target never needs its secret in the browser. The result carries a
  status and a short hint (rejected credentials, wrong path, rate limit, redirect), never the destination's response body.
- **The probe uses the same SSRF guard as delivery, without redirects.** `forwarding-test` is an `AddOutboundGuard`
  client with a 10 s timeout, because it carries the target's credentials.

## Alternatives considered

- **A stored preset kind (`Kind = GrafanaCloud`).** Would let the CLI and Terraform offer it too, but adds a migration and
  a second shape for every consumer of the target for what is only a URL and a header.
- **Probing from Flare.Ingest.** That is where delivery runs, so it would test the exact path, but Ingest has no admin
  API and the dashboard talks only to Flare.Api.

## Consequences

- The test runs from Flare.Api, so `Outbound__Allow` for a private destination must be set on Flare.Api as well as
  Flare.Ingest.
- The probe only shows that the logs path is reachable and the credentials are accepted; a token without the traces or
  metrics scope still passes and fails per signal at delivery, visible in the status table.
- The preset exists only in the dashboard. `flare forwarding create` still takes an explicit endpoint and header.
