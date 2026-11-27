# Flare Helm chart

Official chart for running Flare on Kubernetes. Full guide:
[docs/how-to/deploy/deploy-with-helm.md](../../../docs/how-to/deploy/deploy-with-helm.md).

```bash
helm install flare ./deploy/helm/flare -n flare --create-namespace
```

Deploys ingest, api, alert-worker and dashboard, plus bundled ClickHouse, Redis and
(optionally) Postgres. Every bundled store can be swapped for an external one in
`values.yaml`. ClickHouse schema migrations run inside ingest/api at startup
(`ClickHouseMigrationRunner`), so the chart mounts no SQL. With
`clickhouse.migrationJob.enabled=true` they run in a one-shot Job instead
(`templates/clickhouse-migrate-job.yaml`, ADR-0284), and no service pod gets the `default`
password. That needs an image with the `migrate` command, so it's off by default until a
release ships one.

Checks: `helm lint deploy/helm/flare` (also run in CI by `.github/workflows/helm-lint.yml`).
