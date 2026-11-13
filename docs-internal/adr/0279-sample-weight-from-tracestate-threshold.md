# ADR-0279: Sample weight from the tracestate `th` threshold

Status: Accepted

Date: 2026-11-13

## Context

ADR-0122 gave every stored span a `SampleWeight` so the pre-aggregating views stay unbiased
under Ingest's own sampling. Sampling that happened before Ingest was invisible: a service
whose SDK keeps one trace in ten reported a tenth of its real request rate on Services, the
map and SLOs.

OpenTelemetry's consistent-probability sampling spec records the rate on the span. The
sampler writes a 56-bit rejection threshold `T` into the W3C `tracestate` as `ot=th:<hex>`
(1-14 hex digits, trailing zeros omitted). A span kept with threshold `T` had probability
`(2^56 - T) / 2^56`, so its adjusted count is the inverse. The Java and Go SDKs, the contrib
.NET sampler and the Collector's probabilistic sampler write it. Ingest already stored
`TraceState` but ignored it.

## Decision

- **`OtlpTraceMapper` sets `SampleWeight` from `th`** (`TraceStateThreshold.AdjustedCount`),
  for every span, whether or not Ingest sampling is enabled. Only the first `ot` list-member
  is read. A missing, malformed or over-long value gives weight 1, the old behaviour.
- **The count is rounded to the nearest integer and clamped to `[1, UInt32.MaxValue]`.**
  ADR-0122 chose integer weights so rollup sums are exact. The spec's common thresholds
  (1/2, 1/4, 1/10, 1/100 at its default 4-digit precision) round to their exact N, so the
  rounding only biases unusual rates, and by less than one count per span.
- **Ingest's sampler multiplies, never replaces.** A hash keep stores
  `sdkWeight × N` (saturating at `UInt32.MaxValue`). A tail keep is Ingest weight 1, so the span
  keeps the weight it arrived with. Previously both paths overwrote the weight, which was
  harmless only because every span arrived at 1. The two decisions are independent (the SDK
  compares the trace's randomness to `T`; Ingest hashes the trace id with its own
  finalizer), so the product is an unbiased weight.
- **No migration.** The column, views and weighted percentiles from migration 0051 already
  carry any weight.

## Consequences

- Counts on the rolled-up pages are corrected for SDK- and Collector-side sampling that uses
  consistent probability. Raw span counts in the explorer still show only what was stored,
  as in ADR-0122.
- `SampledCount` (spans with weight above 1) now also counts SDK-sampled spans, so the API
  reads the weighted percentile states for those windows. That's the intended behaviour:
  those spans are a weighted sample.
- A plain ratio sampler (OpenTelemetry .NET's `TraceIdRatioBasedSampler`, which
  `Flare.OpenTelemetry`'s `TraceSampleRatio` installs) writes no `th`, so it stays
  uncorrected.
- A sender that writes a wrong `th` inflates counts for its own spans. `tracestate` is
  sender-controlled like every other span field. The weight is bounded by `UInt32` and only
  affects that sender's service.

## Alternatives considered

- **Store a fractional weight** (Float64). Exact for any rate, but it changes the
  materialized views' `UInt64` sums and every `SampleWeight` reader for a precision gain that
  common rates don't need.
- **Read the legacy experimental `p:` value** (power-of-two only). Superseded by `th` in
  the spec, and no current sampler writes it.
- **Apply `th` only when Ingest sampling is enabled.** SDK-side sampling biases the views
  whether or not Ingest samples, so the weight belongs in the mapper.
