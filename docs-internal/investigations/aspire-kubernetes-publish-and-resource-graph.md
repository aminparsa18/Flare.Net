# Investigation: deploying Flare to Kubernetes via Aspire, for real

Dates: 2026-08-29 (publish verification, `0.2.3`), 2026-08-30 (resource-graph
verification, `0.3.1`), and 2026-09-06 (persistent-storage API, post-Aspire-13.5.3
bump)
Related: ADR-0006 (Kubernetes resource-graph RBAC scoping),
`docs/how-to/run-with-aspire.md`, `docs/reference/aspire-hosting.md`,
`docs-internal/planning/roadmap.md`

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

6. **(2026-09-06) `WithPersistentStorage` (the roadmap's "first-class
   persistent-storage API on `AddFlare`") verified against generated
   manifests.** After bumping
   `Aspire.Hosting.Kubernetes` to `13.5.3-preview.1.26425.3` (the first
   version carrying `AddPersistentVolume`/`WithPersistentVolume`), a scratch
   AppHost calling `AddFlare("flare").WithPersistentStorage(clickHouseVolume,
   redisVolume, identityVolume)` against `aspire publish --publisher k8s`
   produced exactly the expected Helm chart: `flare-clickhouse`,
   `flare-redis`, `flare-ingest`, and `flare-api` all rendered as
   `StatefulSet`s (not `Deployment`s — `flare-dashboard`, uninvolved in any
   volume, correctly stayed a `Deployment`), one `PersistentVolumeClaim`
   template per `AddPersistentVolume` call
   (`flare-clickhouse-data`/`flare-redis-data`/`flare-identity-data`, each
   carrying the storage class/capacity/access-mode the scratch AppHost
   configured), and — notably — **both** the `flare-ingest` and `flare-api`
   `StatefulSet`s reference the *same* `flare-identity-data` PVC by
   `claimName`, confirming the "bind the identity volume once, get it wired
   onto both containers that share it" behavior
   `WithPersistentStorage`'s own remarks document. This pass was manifest
   generation only (`aspire publish --publisher k8s`), not a real deploy —
   see finding #8 for the follow-up real-cluster pass.

7. **(2026-09-06) Discovered: `Console.Error.WriteLine`-based warnings from
   AppHost code no longer reach the operator's terminal during `aspire
   publish`/`aspire deploy`, as of Aspire 13.5.3's new step/pipeline-execution
   CLI UI.** This directly affects the pre-existing ephemeral-storage
   warning (`WarnIfKubernetesStorageIsEphemeral`, unrelated to whether
   `WithPersistentStorage` is called) — its doc comment previously claimed
   (accurately, at the time it was written pre-13.5) that `aspire
   publish`/`aspire deploy` "stream the AppHost process's own stdout/stderr
   straight to the terminal." Confirmed live against `13.5.3` that this is no
   longer true: a `Console.Error.WriteLine` call from a `BeforeStartEvent`
   or `BeforePublishEvent` subscription (both confirmed to still fire
   correctly, in the right order, including under `--publisher k8s`) is
   captured into the CLI's own structured log file
   (`~/.aspire/logs/cli_*.log`, oddly tagged `[FAIL]` despite not being an
   actual failure) instead of being echoed to the terminal's new tree-style
   step UI — the terminal shows only the pipeline's own named steps
   (`validate-compute-environments`, `prepare-deployment-targets-k8s`,
   `before-start`, `publish-k8s`, etc.), nothing from the AppHost's own
   `Console` output. No public Aspire API was found for emitting a step or
   warning into that same tree UI from AppHost code (`Aspire.Hosting`'s
   `Publishing` namespace exposes no activity-reporter/step type) — this
   looks like a genuine capability gap in the new CLI, not something this
   package can route around. **Not fixed** — `WarnIfKubernetesStorageIsEphemeral`
   still uses `Console.Error.WriteLine`, now silently degraded to
   log-file-only visibility rather than removed, since it's still better
   than nothing and the mechanism may well be revisited by a future Aspire
   release.

8. **(2026-09-06) `WithPersistentStorage` confirmed against a real cluster —
   the roadmap's live e2e pass, done.** A fresh local k3d cluster (single
   node, `rancher.io/local-path` as the default `StorageClass`,
   `VolumeBindingMode: WaitForFirstConsumer`) plus a k3d-managed registry,
   driven via `aspire deploy --non-interactive` from the same scratch AppHost
   as finding #6 (all three volumes now on the `local-path` storage class).
   Real results, not just generated YAML:
   - **`aspire deploy` completed successfully** (`helm upgrade --install
     --wait`, real, not `--dry-run`) - `ClickHouse`, `Redis`, `ingest`, and
     `api` `StatefulSet`s plus the `dashboard` `Deployment` all reached
     `1/1 Running`; all three `PersistentVolumeClaim`s reached `Bound`
     (`local-path`, `RWO`, capacities `2Gi`/`1Gi`/`256Mi` as configured).
     `flare-api-service`'s `/health` returned `200` through a real Kubernetes
     `Service`, same as finding #2's Docker-image-based deploy.
   - **ClickHouse data survives a pod delete.** Sent a marker log through
     `flare-ingest-service`'s OTLP HTTP endpoint, confirmed it landed in
     `clickhousedb.logs` via `clickhouse-client` inside the pod, deleted
     `flare-clickhouse-statefulset-0` outright (`kubectl delete pod`, not a
     graceful drain), waited for the StatefulSet controller to recreate it
     (a genuinely new pod - fresh `creationTimestamp`, `RESTARTS: 0`), and
     confirmed the exact same row was still there afterward. This is the
     concrete failure mode `WithPersistentStorage` exists to prevent, and it
     no longer happens.
   - **Redis data survives a pod delete the same way** - set a key, forced
     `BGSAVE`, deleted `flare-redis-statefulset-0`, confirmed the key read
     back correctly from the recreated pod.
   - **The identity volume's shared-PVC question (open since finding #6) is
     answered: yes, on this setup.** `flare-ingest-statefulset-0` and
     `flare-api-statefulset-0` both ran simultaneously for the whole test,
     both mounting `claimName: flare-identity-data` (confirmed via
     `kubectl get pod ... -o jsonpath`), and `sha256sum
     /data/identity/flare-identity.db` inside each pod produced the
     *identical* hash throughout - one real SQLite file, genuinely shared,
     no corruption. **Caveat, not yet resolved**: this cluster is
     single-node, and Kubernetes' `ReadWriteOnce` is a node-scoped
     restriction, not a pod-scoped one - two Pods on the *same* node can
     always mount one RWO PVC concurrently, which is exactly the case this
     tested. Whether this still works with `flare-ingest` and `flare-api`
     scheduled onto two *different* nodes (a real multi-node cluster, where
     most block-storage CSI drivers would refuse a second-node RWO mount
     outright and need `ReadWriteMany` instead) remains unverified - a
     single-node k3d cluster cannot exercise that path at all.
   - **Two friction points along the way, both self-inflicted by the local
     k3d test harness, not bugs in `Aspire.Hosting.Flare`**: (a) the
     `AddContainerRegistry` endpoint had to be `localhost:5500` (reachable
     from the host process doing the `docker push`), which is unreachable
     from *inside* the k3d node's containerd - worked around with `k3d image
     import` to sideload the exact pushed tag directly into the cluster's
     image store (the same workaround this repo's own earlier k3d sessions
     used, per leftover `localhost:5050`/`:5555`-tagged images found still
     cached on this machine from 2026-08-29/08-30). Real cloud registries
     don't have this problem - one DNS name is reachable from both sides.
     (b) Deleting the Redis pod for the persistence check above wiped its
     Streams consumer groups (`flare:logs`/`flare:metrics`/`flare:spans` -
     `WithPersistence`'s 30s/100-key flush interval hadn't captured them
     yet), which crashed `flare-ingest` once
     (`HostOptions.BackgroundServiceExceptionBehavior = StopHost`) - it
     self-healed on Kubernetes' automatic restart (idempotent
     `XGROUP CREATE ... MKSTREAM` at startup). Worth knowing as a real
     Flare.Ingest resilience characteristic on *any* Redis restart
     (Kubernetes or otherwise), but orthogonal to persistent storage and out
     of scope for this investigation.

   Cleaned up after: `helm uninstall`, `k3d cluster delete`, `k3d registry
   delete`, the leftover Docker network, and the `k3d` Homebrew formula
   itself - nothing from this pass was left running.

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
- Persistent storage for Kubernetes deployments now has a first-class API
  (`WithPersistentStorage`, finding #6), and the live e2e pass (finding #8)
  confirmed the main claims for real: PVCs bind, pods reach `Running 1/1`
  with the volume mounted, and ClickHouse/Redis data both survive a pod
  delete. What's still genuinely open: finding #8's single-node k3d cluster
  proved the shared identity `ReadWriteOnce` PVC works when `flare-ingest`
  and `flare-api` land on the *same* node (Kubernetes' RWO is node-scoped,
  not pod-scoped) - whether that still holds with the two scheduled onto
  *different* nodes on a real multi-node cluster (where most block-storage
  CSI drivers would refuse it, needing `ReadWriteMany` instead) remains
  unverified, and would need either a multi-node cluster or a `PodAntiAffinity`
  rule forcing them apart to actually test.
- The `Console.Error.WriteLine`-based ephemeral-storage warning's terminal
  visibility regressed under Aspire 13.5.3's new CLI (finding #7) — worth
  revisiting once/if Aspire exposes a public API for emitting a warning or
  step into its new pipeline-execution tree UI from AppHost code.