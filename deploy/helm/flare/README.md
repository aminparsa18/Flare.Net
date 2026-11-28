# Flare Helm chart

Official chart for running Flare on Kubernetes. Full guide:
[docs/how-to/deploy/deploy-with-helm.md](../../../docs/how-to/deploy/deploy-with-helm.md).

```bash
helm install flare ./deploy/helm/flare -n flare --create-namespace
```

Deploys ingest, api, alert-worker and dashboard, plus bundled ClickHouse, Redis and
(optionally) Postgres. Every bundled store can be swapped for an external one in
`values.yaml`. ClickHouse schema migrations run in a one-shot Job
(`templates/clickhouse-migrate-job.yaml`, ADR-0284), the api image run as `migrate`, so the
chart mounts no SQL and no service pod gets the `default` password. The chart falls back to
migrating inside ingest/api at startup (`ClickHouseMigrationRunner`), with a line in
`NOTES.txt`, for an `image.tag` release older than 0.7.0 or when the services have no
per-user connection strings (`flare.migrationJobFallbackReason` in `_helpers.tpl`,
ADR-0285).

Checks: `helm lint deploy/helm/flare` (also run in CI by `.github/workflows/helm-lint.yml`).
