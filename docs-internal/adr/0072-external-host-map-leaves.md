# ADR-0072: External-host leaf nodes on the Service Map

Status: Accepted

Date: 2026-09-28

Supersedes the "Service Map is deliberately unchanged" section of
[ADR-0071](0071-external-api-monitoring.md).

## Context

The Services Map attributes each span to an effective service
(`peer.service`, else `ServiceName`) and draws an edge wherever a span's
parent has a different one. .NET's `HttpClient` never sets `peer.service`,
so a call to Stripe is attributed to the caller and draws nothing. ADR-0071
covered "which hosts do we call" with the `/external-apis` page, but left
the Map alone. Attributing every client span to its `server.address` would
put a hostname node (`orders-api:8080`) between each pair of instrumented
services. To draw a host only when it's external, the query has to know
that the client span has no instrumented child.

## Decision

### An external leaf is an outbound call with no child span

A third Map query takes outbound calls (ADR-0071's
`OutboundCallCondition`: `CLIENT`, no `db.system`/`messaging.system`) with
no `peer.service` and a non-empty domain (`DomainExpr`), and drops every
one whose `(TraceId, SpanId)` appears as some span's
`(TraceId, ParentSpanId)` (`LEFT ANTI JOIN`). What's left is grouped by
caller and domain. The result is merged into the existing lists
(`ServiceDependencyQueryBuilder.MergeExternalLeaves`): one edge per
(caller, host), one node per host with `IsExternal = true`. A host with
the same name as a service node gets only the edge.

The child side is bounded by the window widened 5 minutes at each end
(clock skew between caller and callee), so it reads 0025's `StartTime`
projection. Resource-attribute chips apply to the caller only. A callee in
another environment still answers the call, so it isn't an external host.

Known false leaves: a call whose callee span was sampled out, or not
flushed yet when the query ran, shows as a hostname node. Leaves are capped
at 50 (caller, host) rows, busiest first, so per-tenant hostnames can't
flood the graph.

### A narrow `outbound_calls` table, not the live query

Over `spans`, the client side of that join reads the whole table: `spans`
is `TraceId`-ordered, and the `StartTime` projection has no `Kind`, `Name`
or `StatusCode`. On 3M synthetic spans over 7 days, a 15-minute Map load
read 3.0M rows / 127 MiB for this query, against 44K rows for the edges
query. The Map polls, so that would undo ADR-0043.

Migration 0035 adds `outbound_calls` (`StartTime`-ordered: `TraceId`,
`SpanId`, `ServiceName`, `Domain`, `Name`, `StatusCode`, `DurationNano`),
filled by a materialized view with the same filter and domain expression.
A unit test checks both migration variants contain the expressions
verbatim. The same query over it read 18.7K rows / 588 KiB in 25 ms, with
identical leaves.

Rejected:

- **Widening the projection** with `Kind`, `Name` and `StatusCode`. That
  means drop, re-add and re-materialize a projection over every part of
  `spans`, a heavy one-time rewrite on existing installs, and a new shape
  for the cluster migration runner.
- **Filtering the client side by `TraceId IN (window's traces)`** to use
  the primary key. Trace ids are random, so a window's traces touch nearly
  every granule and nothing gets pruned.
- **Pre-aggregating the leaves.** The anti-join needs span-level rows.

`outbound_calls` has no `ResourceAttributes`. A Map request with filter
chips, or with `ServiceDependencyMetrics:Enabled = false`, runs the live
query over `spans` instead, the same switch as the nodes (ADR-0031). Only
spans flushed after the migration are captured, with no backfill. The Map
window is 24 hours at most, so the gap closes within a day.

### Dashboard

External nodes use the same card with a globe icon and an "External"
label in the header. The label sits in the header, not the badge row,
because an extra badge wraps that row and dagre lays nodes out at a fixed
height. Clicking an external node opens
`/external-apis?domain=<host>&window=<preset>`, which opens that domain's
sheet. A host has no spans of its own for the call-breakdown dialog to
show.

## Consequences

- `HttpClient` calls to third parties show up on the Map with no
  instrumentation change. Internal calls stay service-to-service edges.
- An external node's counts are the calls to it. Those calls are also
  counted in the caller's own node, unlike `peer.service` spans, which move
  to the peer's node. This avoids subtracting from the pre-aggregated
  `service_dependency_nodes`.
- Every outbound call is stored twice: once in `spans`, and as a narrow
  row in `outbound_calls`. No TTL, same as every other table until the
  retention roadmap item lands.
- `outbound_calls` could also back the `/external-apis` page if its live
  queries get slow (ADR-0071's stated follow-up), though it lacks the
  attributes the endpoint and status-code breakdowns need.
