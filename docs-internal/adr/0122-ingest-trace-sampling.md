# ADR-0122: Head and tail trace sampling at ingest

Status: Accepted

Date: 2026-10-05

## Context

Every span an app sends is stored. For a busy service that is the dominant storage and
Redis-buffer cost, and most of it is healthy traces nobody opens. The usual lever is
sampling, but sampling in front of Flare has two traps. Dropping spans naively loses the
traces that matter (errors, slow requests), and Flare's pre-aggregations (ADR-0030 RED,
ADR-0031 and ADR-0043 service map, ADR-0072 outbound calls, ADR-0102 LLM calls, ADR-0108
SLIs) are materialized views that count rows on insert, so a sampled service would
under-report its own request rate.

## Decision

- **Sampling runs in `Flare.Ingest`, before the Redis buffer**, as a decorator over
  `ISpanEventSink` (`SamplingSpanEventSink`) around `TraceSampler`. It is registered only
  when `Sampling:Enabled` is true; off, the plain Redis sink is used and nothing changes.
- **A trace is held in process memory for `HoldWindow` (default 30 s)** after its first span
  arrives, bounded by `MaxHeldSpans`. An error status, or a duration at or above the slow
  threshold, on any span keeps the whole trace: held spans are released, later spans pass
  straight through, and every one is stored with weight 1. Otherwise, when the window ends,
  each span is kept iff `hash(traceId) % N == 0` and stored with weight N. The hash is a
  fixed FNV-1a with a murmur finalizer, not `string.GetHashCode`, so every replica makes the
  same head decision. Verdicts are remembered for `DecisionTtl` so late spans follow them;
  a late error or slow span of an already-dropped trace is still kept.
- **Policies resolve per span** from `Sampling:Rules` (by `service.name`, by ingest key id,
  or both; most specific wins) over the defaults. The ingest key comes from the request's
  `IngestKeyUsageFeature` (ADR-0051) and is captured with the span, so background release
  needs no request. Because each span uses its own service's N, a trace can be partially
  kept when services have different rates; that is the per-service rule doing what it says.
- **Weights are integers, so sampling is "1 in N".** `spans.SampleWeight UInt32 DEFAULT 1`
  (migration 0051, plus cluster variant) records how many real spans a stored span stands
  for. Integer weights make the rollup counts exact sums; a fractional rate would need
  rounding per span. The row mapper floors the value at 1, because a payload buffered before
  the field existed decodes it as 0 through the JSON fallback.
- **The materialized views sum the weight instead of counting rows** (`MODIFY QUERY`, as in
  0034/0037): RED, node and call-breakdown counts, outbound calls (which gains
  `SampleWeight`), LLM calls and tokens, and SLI buckets. The two strata (tail-kept traces
  at weight 1, hash-kept spans at weight N) are each unbiased, so their sum is.
- **Percentiles get new weighted columns, not a changed type.** A `quantile` state can't
  take weights and an `AggregateFunction` column's type can't be altered in place, so each
  quantile table gains `quantileTDigestWeighted` states and `SampledCount` (spans with weight
  above 1). The original states stay filled. `SampledQuantileSql` makes the API read the
  weighted states only for windows where `SampledCount > 0`, so history from before the
  migration keeps its percentiles and nothing changes while sampling is off.
- **Raw-`spans` fallbacks weight too**: the Services, dependency and call-breakdown live
  queries (used with resource-attribute chips) and the LLM live query sum `SampleWeight` and
  use `quantileTDigestWeighted`, so filtered and unfiltered views agree.

## Consequences

- Storage and Redis cost fall roughly by the sampled fraction of healthy traces; error and
  slow traces are retained in full. This is the second storage lever next to retention.
- Rollup counts stay unbiased. Percentiles are t-digest estimates over the weighted
  stratified sample.
- **Raw span counts do not.** The Traces explorer, span search, facets, waterfall and any
  `count()` over `spans` show only what was stored. A sampled service's trace count is lower
  than its request rate by design.
- **State is per replica.** An error discovered on one replica does not release spans held
  on another; with several ingest replicas, route a trace's spans to one (an otelcol
  `loadbalancing` exporter keyed by trace id). Head decisions agree regardless.
- **A crash loses the held spans** (up to `HoldWindow` of healthy traffic and any trace
  about to be kept). A graceful stop decides all held traces first. This is the same loss
  window as any in-memory stage ahead of the stream; ADR-0002's durability starts at the
  Redis write.
- A trace whose first error arrives after `HoldWindow` is decided as healthy. Raise the
  window for slow batch jobs, or give those services `KeepOneIn: 1`.
- Percentiles in a window that mixes pre-migration and sampled buckets come from the
  weighted states alone, which cover only post-migration spans.
- `topK` top operations in the service-map nodes remain unweighted; the ranking is
  proportional to the sample, which is close enough for a top-3.

## Alternatives considered

- **Sampling in the OTel collector.** The collector's `tail_sampling` processor is mature
  and is the right answer for teams already running one. Flare still needs the weights to
  correct its rollups, and many Flare users send straight from the SDK, so the sampler lives
  here too.
- **Keep every span, drop later (ClickHouse TTL on a keep flag).** The views would see
  everything and need no weights, but the tail verdict arrives after insert, the saving only
  appears after a merge, and Redis and ClickHouse still take the full write load.
- **Never sample root/entry spans** so RED stays exact. Cheaper, but the map, call
  breakdown, outbound and LLM views are child-span rollups and would still skew, and the
  saving is small.
- **Hold decisions in Redis** so replicas share tail verdicts. Correct across replicas but
  adds a round trip per span and a hold store to size; deferred until per-replica state
  proves a problem.
- **Fractional sample rates / probabilistic weights.** Needs rounding per span in `UInt64`
  sums or Float64 aggregates; integer 1-in-N is exact and covers the useful range.
