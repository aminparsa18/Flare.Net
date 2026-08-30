# ADR-0006: Kubernetes resource-graph RBAC is scoped to `pods`/`services` only

Status: Accepted
Date: 2026-08-30 (v21, Kubernetes resource-topology provider)

## Context

On a Kubernetes deployment target, the Resources page's Docker-socket-proxy
mechanism (ADR-0005) doesn't apply — there's no `/var/run/docker.sock`
equivalent inside a pod. A second, independent topology provider was built
speaking Kubernetes' own shape (Namespace → Deployment → Pod, plus
Service) instead of faking Docker-shaped data out of the Kubernetes API.
That provider needs read access to cluster objects, which on Kubernetes
means an RBAC `ServiceAccount`/`Role`/`RoleBinding` — and RBAC scope is a
real design choice: broader permissions mean richer, more accurate data;
narrower permissions mean less attack surface if `flare-api` is ever
compromised.

## Decision

`enableResourceGraph: true` on a Kubernetes target attaches a
**namespace-scoped, read-only** `Role` granting `get`/`list`/`watch` on
**`pods` and `services` only** — no `deployments`/`replicasets` permission
at all, and a `Role` (not a `ClusterRole`), so there's nothing visible
beyond Flare's own namespace. `KubernetesResourcePoller` **synthesizes**
the "Deployment" grouping layer in the graph by reading each Pod's own
`flare.role` label, rather than making a live call to the real Deployments
API.

## Alternatives considered

- **Also request `deployments`/`replicasets` read access**, and read the
  real Deployment objects for the grouping layer. Rejected: it would make
  a synthesized Deployment node's replica count and rollout status
  *real* data instead of inferred, but at the cost of a broader RBAC
  surface for every `enableResourceGraph: true` deployment — judged not
  worth it for a topology-visualization feature, where "this Pod belongs
  to this logical group" is the information that actually matters, not
  live rollout state.
- **A `ClusterRole` instead of a namespace-scoped `Role`**, to make the
  RBAC object reusable across namespaces or simpler to reason about
  cluster-wide. Rejected: Flare's own resource graph only ever needs to
  see its own namespace, so a `ClusterRole` would grant strictly more
  reach than the feature uses.

## Consequences

- **The Deployment node in the graph is synthesized, not authoritative.**
  Replica count and rollout status are not shown and cannot be added
  without revisiting this decision — this is a deliberate trade-off, not
  an oversight, and should be stated as such wherever the Resources page's
  Kubernetes behavior is documented for users.
- Verified live (2026-08-30) against a real k3s cluster: RBAC applied
  cleanly with zero permission errors in `flare-api`'s logs, and
  `GET /api/resources/snapshot` returned the complete graph (16 nodes: 1
  Namespace, 5 synthesized Deployment groups, 5 Pods, 5 Services) —
  confirming the scoped-down permission set is sufficient for the feature
  as designed, not merely assumed sufficient.
- Getting to this working state surfaced Kubernetes-specific bugs unrelated
  to the RBAC scope decision itself (a label-value charset violation, an
  RBAC-object naming collision, a `StatefulSet` label gap) — see
  [the investigation](../investigations/aspire-kubernetes-publish-and-resource-graph.md)
  for the evidence and fixes.

## Related documentation

- `docs/how-to/run-with-aspire.md` — enabling `enableResourceGraph` on a
  Kubernetes target
- ADR-0005 — the Docker-side equivalent decision (different mechanism,
  same "scope access as tightly as practical" posture)
- `docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md`