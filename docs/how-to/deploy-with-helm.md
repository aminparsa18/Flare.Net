# How to deploy Flare on Kubernetes with Helm

Install the whole stack - ingest, API, alert worker and dashboard, with ClickHouse
and Redis - into a Kubernetes cluster using the chart in
[deploy/helm/flare](../../deploy/helm/flare).

## Prerequisites

- A cluster, `kubectl` and Helm 3.
- A default StorageClass (or set the `*.storageClass` values) for the bundled
  ClickHouse, Redis and identity volumes.
- This repository cloned, or a release of the chart from the OCI registry: replace
  `./deploy/helm/flare` below with `oci://ghcr.io/aminparsa18/charts/flare` and add `--version <x.y.z>`
  (published from each release tag).

## Try it with port-forwards

1. Install:

   ```bash
   helm install flare ./deploy/helm/flare -n flare --create-namespace --wait --timeout 10m
   ```

2. Forward the dashboard and API. The dashboard is a client-rendered app that
   calls the API from your browser, so both are needed:

   ```bash
   kubectl -n flare port-forward svc/flare-flare-dashboard 7777:3000 &
   kubectl -n flare port-forward svc/flare-flare-api 8080:8080 &
   ```

3. Open <http://localhost:7777> and create the admin account.

4. Point apps in the cluster at `flare-flare-ingest.flare:4317` (gRPC) or
   `http://flare-flare-ingest.flare:4318` (HTTP).

Passwords for ClickHouse, Redis and Postgres are generated on first install and
kept across `helm upgrade`.

## Expose it with an Ingress

Set the public address once. It feeds the dashboard's origin, the API's CORS
allow-list and the links in alert notifications:

```yaml
# values.yaml
publicUrl: https://flare.example.com
ingress:
  enabled: true
  className: nginx
  host: flare.example.com
  tls:
    - secretName: flare-tls
      hosts: [flare.example.com]
```

`/api` and `/mcp` route to the API, everything else to the dashboard. The live
tail is a WebSocket, so the controller must allow upgrades (ingress-nginx does).

To receive OTLP from outside the cluster, enable `ingress.otlpHttp` and/or
`ingress.otlpGrpc` with their own hosts. The gRPC one needs a controller that
speaks gRPC; the default annotation is the ingress-nginx one.

### Under a sub-path

Add `basePath: /flare` and keep `publicUrl` at the origin (without the path).
The chart sets `Flare__BasePath`, `FLARE_BASE_PATH` and the matching URLs, as in
[serve under a sub-path](serve-under-a-sub-path.md), and the Ingress paths gain
the prefix without stripping it.

## Choose the identity store

| `identity.provider` | Use when | Notes |
| --- | --- | --- |
| `Sqlite` (default) | Small or single-node installs | One PVC shared by ingest and api. With the default `ReadWriteOnce`, the chart schedules both onto the same node. Set `identity.sqlite.accessMode: ReadWriteMany` if your storage supports it. |
| `Postgres` | Multiple nodes or HA ([ADR-0111](../../docs-internal/adr/0111-pluggable-identity-store-postgres.md)) | Bundled single-instance Postgres, or `identity.postgres.enabled: false` with `identity.postgres.external.connectionString` (or `.existingSecret`). |

## Use an existing ClickHouse or Redis

Disable the bundled store and point at yours:

```yaml
clickhouse:
  enabled: false
  external:
    host: clickhouse.data.svc
    username: default
    clusterMode: false
  existingSecret: my-clickhouse     # key: clickhouse-password
redis:
  enabled: false
  external:
    host: redis.data.svc
  existingSecret: my-redis          # key: redis-password
```

For a ClickHouse cluster (for example one run by the Altinity operator), set
`clusterMode: true` and read [clustering](../explanation/clustering.md) for the
server-side prerequisites. The chart does not install the operator.

Passwords are placed into connection strings, so avoid `;` in them.

## Scale and tune

- `ingest.replicas > 1` also turns on shared pattern-cluster state
  (`LogPattern__SharedStore`). Use `identity.provider: Postgres` or a
  `ReadWriteMany` volume so replicas on different nodes can share identity.
- Keep `alertWorker.replicas` at 1.
- Every component accepts `resources`, `nodeSelector`, `tolerations` and
  `extraEnv` (any `Section__Key` setting, such as `Query__*` caps).
- Email alerts: set `email.host`, `email.from` and so on, with the password in
  `email.password` or a Secret named by `email.existingSecret` (key
  `smtp-password`).

## Upgrade

```bash
helm upgrade flare ./deploy/helm/flare -n flare -f values.yaml
```

Schema migrations run in ingest and api on startup, so there is no migration
job to run. Bump `image.tag` to move versions; it defaults to the chart's
`appVersion`.

The chart pins ClickHouse to the `26.8` LTS line (`clickhouse.image`) instead of
`:latest`, so an upgrade never changes the ClickHouse version on its own. Move to a
newer line deliberately by setting `clickhouse.image` yourself. ClickHouse cannot
be downgraded afterwards. If you ran `:latest` on a newer line before this pin,
set `clickhouse.image` to the line you are running rather than letting it fall back
to 26.8.

## Relationship to Aspire's generated charts

`aspire publish` with `AddKubernetesEnvironment()` (see
[Aspire's Helm chart docs](https://aspire.dev/deployment/kubernetes/helm-charts/))
generates a chart for *your* AppHost's resources. This chart is Flare's own,
independent of any AppHost. A consumer AppHost that wants Flare in the same
cluster can install this chart alongside its own with `AddHelmChart`, using the OCI
chart above.
