# Flare Helm chart

Official chart for running Flare on Kubernetes. Full guide:
[docs/how-to/deploy-with-helm.md](../../../docs/how-to/deploy-with-helm.md).

```bash
helm install flare ./deploy/helm/flare -n flare --create-namespace
```

Deploys ingest, api, alert-worker and dashboard, plus bundled ClickHouse, Redis and
(optionally) Postgres. Every bundled store can be swapped for an external one in
`values.yaml`. ClickHouse schema migrations run inside ingest/api at startup
(`ClickHouseMigrationRunner`), so the chart mounts no SQL.

Checks: `helm lint deploy/helm/flare` (also run in CI by `.github/workflows/helm-lint.yml`).
