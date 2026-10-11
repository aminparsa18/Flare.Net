# Sampling, clustering and cost

What decides how much Flare costs to run, and what does it do to keep that cost
down without making your numbers wrong? This page explains trace sampling and
why sampled spans carry a weight, why log patterns are computed once at ingest,
what drives storage, and what promoted attribute columns trade away. For the
steps, see the how-to guides linked at the end.

## What drives storage

Storage cost follows the volume and the *shape* of the data you send:

- **Event volume per signal.** Logs, spans and metric points are the unit. A few
  chatty services usually account for most of it. The Usage page ranks services,
  ingest keys and attributes by what they store.
- **Wide attribute maps.** Every attribute key and value on every row is stored,
  so a large attribute that nobody queries still costs you on every event.
- **High cardinality.** A metric attribute with many distinct values (a URL, a
  user id, a request id) multiplies the number of series. Each series is its own
  set of rows, so one such attribute can dwarf everything else about the metric.
- **Retention.** Data costs until it expires; see
  [Retention and tiering](retention-and-tiering.md).

Flare gives you levers at three different points, because the right place to
shed data depends on what you're shedding: trace *sampling* drops healthy traces
before they are stored, *pipeline rules and attribute reduction* shrink what is
stored per event, and *retention* limits how long it stays.

For metrics, Flare can drop or keep chosen data-point attributes at ingest, so a
`user.id` on a counter doesn't create a series per user. This happens just
before the batch is written, so the buffer still holds the original point and a
bad rule can be fixed without losing it. The cost is that it is irreversible for
data it applies to, and reducing a *cumulative* metric is lossy (collapsed series
interleave, which can look like counter resets), so prefer it for delta metrics,
gauges and histograms
([ADR-0083](../../docs-internal/adr/0083-metric-attribute-reduction.md)).

## Trace sampling, and why spans carry a weight

Most stored spans belong to healthy traces nobody opens. Sampling keeps a
fraction of them. Two things make naive sampling dangerous, and Flare's design
answers both.

**Sampling must not lose the traces that matter.** Flare samples in
`Flare.Ingest`, before the buffer, and decides per trace rather than per span. It
holds a trace in memory for a short window after its first span arrives. If any
span has an error, or is slower than a threshold, the whole trace is kept. Only
otherwise does the "keep 1 in N" decision apply, using a hash of the trace id so
every replica agrees. Rules can set N and the slow threshold per service or per
ingest key, with the most specific match winning.

**Sampling must not make counts wrong.** Flare precomputes request rates, error
counts, the service map, outbound calls, LLM usage and SLO counters with
materialized views that count rows as they are inserted
([ADR-0030](../../docs-internal/adr/0030-service-red-metrics-pre-aggregation.md)).
If a service keeps one span in ten, those views would report a tenth of its real
traffic. So each stored span records a **sample weight**, the number of real
spans it stands for. The views *sum the weight* instead of counting rows. Tail-kept
traces are stored at weight 1 and hash-kept spans at weight N, and each group is
an unbiased estimate, so the sum is too. Weights are whole numbers ("1 in N")
because integer sums stay exact, where a fractional rate would need rounding per
span. Percentiles use weighted t-digest states, read only for time ranges that
contain sampled data, so history from before sampling keeps its original
percentiles.

What sampling cannot fix, and you should expect:

- **Raw span counts are lower than reality.** The Traces explorer, span search,
  facets, the waterfall and any plain count over spans show what was stored. The
  rolled-up pages (Services, the map, SLOs) are corrected; the explorer is not.
- **Held state is per process.** An error seen on one replica doesn't rescue
  spans held on another. With several Ingest replicas, route a trace's spans to
  one of them, for example with an OpenTelemetry Collector load-balancing
  exporter keyed by trace id.
- **A crash loses the held spans**, up to the hold window of traffic. A graceful
  stop decides every held trace first.
- **A trace whose first error arrives after the hold window** is judged healthy.
  Raise the window, or exempt slow batch jobs.

Sampling is off by default. See
[ADR-0122](../../docs-internal/adr/0122-ingest-trace-sampling.md) for the
alternatives (collector-side sampling, holding decisions in Redis) and why
they weren't chosen.

## Log patterns are computed once, at ingest

The Logs page can group messages into patterns, so `GET /api/orders/123` and
`GET /api/orders/456` become `GET /api/orders/<*>` with an exact occurrence
count. Flare clusters messages with the Drain algorithm in the flush worker, just
before the batch is written to ClickHouse, and stores a pattern id and template
on each row
([ADR-0007](../../docs-internal/adr/0007-pattern-clustering-at-flush-time.md)).
Reading patterns is then an ordinary `GROUP BY`.

The alternative, clustering at query time, would read and tokenize the body of
every matching row on every page load, or sample and give up the exact counts.
Paying the cost once at insert, in a worker that is already doing the work, makes
the read side cheap and the counts exact. The trade-offs:

- **Ingest does the work.** Clustering adds CPU to the flush path. It can be
  switched off with a setting if that ever matters.
- **Each replica has its own picture by default.** With several Ingest replicas,
  the same template can get different ids on each; a shared Redis-backed store
  fixes that (see [Clustering](clustering.md)).
- **No backfill.** Rows written before the feature have no pattern and are left
  out of the ranked list rather than shown as "unknown".

## High cardinality and promoted columns

Every log or span attribute lives in a map column, and filtering on one reads the
keys and values of each row in the scanned blocks. For a common key such as
`http.route` that is slow, because the table-wide skip indexes can almost never
rule a block out. An admin can **promote** a key: Flare adds a materialized
column for it with its own skip index, and filters use the column instead of the
map
([ADR-0062](../../docs-internal/adr/0062-promoted-attribute-columns.md) for
logs, [ADR-0063](../../docs-internal/adr/0063-promoted-span-attribute-columns.md)
for spans).

Because which keys are hot depends on your deployment, promotion is a runtime
action and not a migration, and the table's own schema is the registry, so it
can't drift from the DDL that actually ran. The trade-offs are real:

- **It changes speed, never results.** Only operators that give identical
  answers switch to the column; the ones where "missing" and "empty" would differ
  keep the map form.
- **It costs on every insert.** Each promoted key adds a column and an index, so
  the number is capped per table.
- **It costs storage.** The value is stored a second time.
- **Backfill is optional.** Without it, only new data is stored in the column;
  results are still correct because older data is computed on read.
- **Only filters benefit.** Group-by and value autocomplete still read the map.
- **Demotion is immediate for the instance that runs it**, but other API
  instances can reference the dropped column for up to a refresh interval, and
  queries they run in that window can fail until they refresh.

## Estimated LLM cost

For services that emit GenAI spans, the LLM page shows tokens per model and an
*estimated* cost, using per-million-token prices. Spans carry token counts and
never prices, so Flare ships list prices for common models and lets an admin
override any of them, which keeps price data in configuration instead of in the
telemetry. A model with no known price shows no cost rather than zero, which
would read as free. It is an estimate: Flare can't see cached-input discounts,
batch pricing or negotiated rates
([ADR-0101](../../docs-internal/adr/0101-llm-estimated-cost.md)).

## Related

- Explanation: [How Flare stores and queries telemetry](how-flare-stores-and-queries-telemetry.md), [Retention and tiering](retention-and-tiering.md), [Clustering](clustering.md)
- How-to: [See what drives storage](../how-to/data/see-what-drives-storage.md), [Find high-cardinality metrics](../how-to/data/find-high-cardinality-metrics.md), [Reduce metric attributes](../how-to/data/reduce-metric-attributes.md), [Promote attribute columns](../how-to/data/promote-attribute-columns.md), [Monitor LLM calls](../how-to/monitor/monitor-llm-calls.md)
- ADRs: [0007](../../docs-internal/adr/0007-pattern-clustering-at-flush-time.md), [0062](../../docs-internal/adr/0062-promoted-attribute-columns.md), [0063](../../docs-internal/adr/0063-promoted-span-attribute-columns.md), [0083](../../docs-internal/adr/0083-metric-attribute-reduction.md), [0101](../../docs-internal/adr/0101-llm-estimated-cost.md), [0122](../../docs-internal/adr/0122-ingest-trace-sampling.md)
