# Investigation: deploying Flare to Kubernetes via Aspire, for real

Dates: 2026-08-29 (publish verification, `0.2.3`) and 2026-08-30 (resource-graph
verification, `0.3.1`)
Related: ADR-0006 (Kubernetes resource-graph RBAC scoping),
`docs/how-to/run-with-aspire.md`, `docs/reference/aspire-hosting.md`

## Problem statement

`Aspire.Hosting.Flare` claims to support publishing/deploying a Flare-
containing AppHost to Kubernetes via `aspire publish`/`aspire deploy`. Both
passes below tested that claim against a real cluster rather than trusting
artifact generation alone — first whether the deploy works at all, then
(once it did) whether the Kubernetes-specific resource-topology provider
actually works against a real API server.

## Environment

A local [k3s](https://aspire.dev/integrations/compute/k3s/) cluster running
in Docker, with a local insecure registry mirror for the generated
ClickHouse-init image. A throwaway scratch AppHost outside this repo
(`AddKubernetesEnvironment` + `AddContainerRegistry`, referencing
`Aspire.Hosting.Flare`), driven via `aspire publish -o k8s-artifacts` and
`aspire deploy`.

## Findings

1. **(2026-08-29) Before `0.2.3`, this could never work at all, for any
   consumer using the standard Aspire AppHost naming convention.**
   Aspire's Kubernetes publisher builds `WithDataVolume()`'s *default*
   volume name from the AppHost project's own name without sanitizing it
   for Kubernetes' DNS-1123 naming rules — a `.AppHost`-suffixed project
   name (the standard template convention; this repo's own `Flare.AppHost`
   and the scratch AppHost both use it) produces a volume name containing
   a dot, which Kubernetes rejects outright (`must not contain dots`).
   ClickHouse's and Redis's `StatefulSet`s could never create a pod —
   confirmed live, this failed every time until fixed. **Fixed**:
   `AddFlare` now passes an explicit, dot-free name to both
   (`{name}-clickhouse-data`/`{name}-redis-data}`), the same pattern the
   identity volume already used. Nothing to do on the consumer side — this
   was purely an `Aspire.Hosting.Flare` bug.

2. **(2026-08-29) Full deploy confirmed working after the fix above.**
   `aspire deploy` against the k3s cluster: the full stack (ClickHouse and
   Redis `StatefulSet`s included) reached `Running 1/1`, ClickHouse ran its
   init SQL, ingest/api connected to Redis and started listening, and
   `flare-api`'s `/health` returned `200` through a real Kubernetes
   `Service` — a real `helm upgrade --install --wait` against a live
   cluster, not just artifact generation.

3. **(2026-08-30) A Kubernetes label VALUE has a strict charset** (roughly
   alphanumeric/`-`/`_`/`.` only) that a
   `"clickhouse:Reference,redis:Reference"`-shaped `flare.relationships`
   value violates outright — `helm upgrade --install` rejected the whole
   Deployment as invalid on first attempt. Docker labels have no such
   restriction, so this never surfaced there. **Fixed**:
   `flare.relationships` goes onto pod-template *annotations* instead of
   labels on the Kubernetes side only (annotations have no charset
   restriction, and this value was never selected on anyway — only
   `flare.resource`/`flare.role` are).

4. **(2026-08-30) The generated RBAC `ServiceAccount`/`Role`/`RoleBinding`
   all shared the same `Metadata.Name`, and Aspire's per-object
   Helm-chart-template-file naming keys purely off that name, not
   name+kind** — each `AdditionalResources.Add` call silently overwrote
   the previous one's rendered template file, so only the last one added
   (`RoleBinding`) actually made it into the chart. `flare-api`'s own
   `ReplicaSet` couldn't create pods at all: `error looking up service
   account default/flare-resource-graph: serviceaccount ... not found`.
   **Fixed**: each of the three RBAC objects now gets a distinct name.

5. **(2026-08-30) ClickHouse/Redis got zero `flare.*` labels at all under
   Kubernetes, invisible to the topology graph entirely.** Their
   `WithDataVolume()` calls promote them to a `StatefulSet` (Aspire's own
   behavior — see finding #1), which the original
   `resource.Workload is Deployment` pattern match in
   `WithFlareResourceLabels` silently skipped. Confirmed live via
   `kubectl get pod ... -o jsonpath='{.metadata.labels}'` showing only
   Aspire's own `app.kubernetes.io/*` labels, no `flare.*` at all.
   **Fixed**: reads the common `Workload.PodTemplate` (declared on the
   shared base type both `Deployment` and `StatefulSet` derive from)
   instead of pattern-matching `Deployment` specifically.

## Conclusion

After all three 2026-08-30 fixes (rebuilt `Flare.Api`/`Aspire.Hosting.Flare`,
built and pushed a local `flare-api` image to the registry mirror since the
published `xracer007/flare-api:edge` tag lagged behind these same-day
fixes, redeployed): all five pods (`clickhouse`, `redis`, `ingest`, `api`,
`dashboard`) reached `Running 1/1`, RBAC applied cleanly with zero
permission errors in `flare-api`'s logs, and `GET /api/resources/snapshot`
returned the complete real graph — 16 nodes (1 Namespace, 5 synthesized
Deployment groups, 5 Pods, 5 Services), all 5 `Selects` edges, and all 5
`Reference` edges from `flare.relationships`. Also confirmed working, no
bugs found: `aspire deploy` sets `IsPublishMode` the same way `aspire
publish` does (the `aspire run`-safety check fired correctly), and the RBAC
`RoleBinding` subject's `{{ .Release.Namespace }}` Helm-templated string
survives Aspire's YAML serialization and resolves correctly.

None of findings #3-5 were catchable by unit tests alone — all three are
specifically about what Aspire's Kubernetes publisher and a real API server
do with the generated objects, not about this package's own mapping logic.

## Unresolved / follow-ups

- Whether `aspire publish` against a Kubernetes target with `imageTag:
  "edge"` correctly emits `imagePullPolicy: Always` is not yet
  root-caused — it showed as `IfNotPresent` during the 2026-08-29
  verification. Only matters for the mutable `edge` tag; a real
  deployment normally pins a stable, immutable tag instead, where this
  doesn't apply.
- Persistent storage for Kubernetes deployments is a known, documented gap
  (`emptyDir`, not `PersistentVolumeClaim`, unless the consumer wires
  `AddPersistentVolume` themselves) — see
  `docs/reference/aspire-hosting.md`'s Kubernetes section. Not a bug found
  during this investigation, but adjacent to it and worth cross-referencing.