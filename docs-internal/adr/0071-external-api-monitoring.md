# ADR-0071: External API monitoring by domain

Status: Accepted

Date: 2026-09-28

## Context

Flare had one view of "what does this service call out to": the Services
Map view's per-node breakdown, whose External-calls tab grouped spans by
`peer.service`. .NET's `HttpClient` instrumentation never sets
`peer.service`. It sets `server.address`, `url.full`,
`http.request.method`, `http.response.status_code` and, on failure,
`error.type`. So a typical .NET app's calls to Stripe, Twilio or another
team's API were missing from that tab, and nothing else in Flare listed
them either. Prior art: SigNoz's external API monitoring
([signoz#7308](https://github.com/SigNoz/signoz/commit/02f3dfefb90b75ccee7ef07b14f903c1dfce5359),
[signoz#7432](https://github.com/SigNoz/signoz/commit/0b7cd4c1a74b8cee2c844f1b6c1374c1f84be447),
top errors per domain in
[signoz b86e65d](https://github.com/SigNoz/signoz/commit/b86e65d2ca78a1f1a4e39680aaf47faa9055a547))
settled on a domain table, per-domain endpoint table, status-code
breakdown, top errors and a "which of our services call this" table.

## Decision

### Which spans are outbound calls

A span is an outbound call when it is a `CLIENT` span (`Kind = 3`) that
names a domain and has no `db.system` or `messaging.system`. Database
and messaging client spans carry `server.address` too, but they already
have their own views (the breakdown's Database tab, the `/messaging`
page, ADR-0056).

The domain is `server.address`, else the pre-1.21 `net.peer.name`, else
the host of `url.full` (else the older `http.url`). Method and status code
read the current attribute first and the older one second
(`http.request.method`/`http.method`,
`http.response.status_code`/`http.status_code`). .NET instrumentations
still emit either generation. The expressions live in
`ExternalApiQueryBuilder` (`DomainExpr`, `OutboundCallCondition`, ...).

### Step 1: the breakdown falls back to the domain

The External-calls tab now groups by `peer.service` when set, else by the
domain of an outbound call (`ServiceCallBreakdownQueryBuilder.ExternalTargetExpr`).
`peer.service` still wins because it is the name the caller chose
deliberately.

The pre-aggregated path (`service_call_breakdown_external`, ADR-0031)
must group the same way, or toggling `ServiceDependencyMetrics` or adding
a filter chip would change the rows. Migration 0034 swaps the view's
query with `ALTER TABLE ... MODIFY QUERY`. That is atomic (inserts never
skip the view, unlike `DROP` + `CREATE`) and idempotent, so the migration
runner can re-run it. The table's columns are unchanged, and the value
still lands in `PeerService`. Rows aggregated before the migration keep
their `peer.service`-only grouping until they age out; that's accepted
rather than backfilled. A unit test checks that both migration variants
contain `ExternalTargetExpr` verbatim.

### The Service Map is deliberately unchanged

The roadmap item also suggested the fallback for the Map view's nodes and
edges. We didn't do it. The Map attributes each span to an "effective
service" (`peer.service`, else `ServiceName`). A client span that calls
another *instrumented* service is followed by that service's own server
span. Attributing the client span to its `server.address` would put a
hostname node (`orders-api:8080`) between every pair of services talking
over `HttpClient`. The graph would get worse for the most common
internal call. Adding external leaves correctly needs to know that a
client span has no instrumented child, which means another self-join in
the already-heaviest query (ADR-0043). It is left as a roadmap item. The
new page and the breakdown cover "which external hosts do we call".

### Step 2: the `/external-apis` page

Two endpoints, same shape as the messaging page:
`POST /api/external-apis/domains` (one row per domain: rate, error rate,
p50/p95/p99, endpoint and caller counts, last seen) and
`POST /api/external-apis/domain-detail` (endpoints, status codes, top
errors, calling services). Both take a window, an optional end and an
optional calling service.

- **Computed from `spans` at query time. No new table.** Same reasoning
  as ADR-0056: every query requires one of the address attributes via
  `mapContains`, which `idx_span_attr_key` can use to skip granules. The
  endpoint grouping is a heuristic we expect to tune, and a materialized
  view would bake one version of it into stored rows. A view is the
  follow-up if this is slow on real data.
- **Endpoints are templated at query time.** The instrumentation's own
  `url.template` is used when set; it is low-cardinality by definition,
  but `HttpClient` only sets it when the caller opts in. Otherwise the
  URL's path is split on `/` and any segment that is all digits, a UUID,
  a 16+ character hex string, or 16+ characters containing a digit
  (`cus_Nffr...`, Twilio's `AC...` account ids) becomes `{id}`. Some ids
  will slip through as their own endpoint. That is a readability cost,
  not a correctness one, since every call is still counted under its
  domain. gRPC calls use `rpc.service/rpc.method`; anything else its span
  name. Each endpoint row says which rule produced it
  (`ExternalEndpointSource`), so the dashboard can build a trace filter
  that matches it.
- **Trace drill-down is a one-condition structural query** (ADR-0069):
  `server.address = <domain>`, plus method, `url.template`, a `url.full`
  regex rebuilt from the templated path (`{id}` → `[^/]+`),
  `rpc.method` or the span name, plus status code or error status for a
  top-errors row. The outbound call is a child span, and the trace list's
  plain attribute filters only match the root. A domain found only through
  `net.peer.name` or the URL won't match the `server.address` filter.
  That is accepted, since current instrumentations all set
  `server.address`.
- **Not built:** per-domain time-series charts, and drilling into traces
  around a chosen point on such a chart. The trace explorer has no custom
  time range to link to yet. These are on the roadmap.

## Consequences

- A .NET app's `HttpClient` and gRPC calls now show up in the Services
  breakdown and on their own page with no instrumentation changes.
- Service-to-service `HttpClient` calls also appear, as calls to the
  callee's host. That is intended: the page is "every host we call", and
  the Map still shows the service-level edge.
- Live queries mean page cost grows with span volume in the window. The
  same 5m-24h window clamp and query safety caps as the messaging page
  apply.
