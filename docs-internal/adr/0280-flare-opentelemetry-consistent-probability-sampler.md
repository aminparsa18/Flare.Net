# ADR-0280: Flare.OpenTelemetry samples with its own consistent-probability sampler

Status: Accepted

Date: 2026-11-13

Supersedes the `Flare.OpenTelemetry` consequence of [ADR-0279](0279-sample-weight-from-tracestate-threshold.md).

## Context

ADR-0279 made Ingest weight spans by the `ot=th:` threshold that consistent-probability
samplers write into `tracestate`. `Flare.OpenTelemetry`'s `TraceSampleRatio` (ADR-0270)
installed `ParentBasedSampler(TraceIdRatioBasedSampler)`, which writes nothing, so apps using
Flare's own package were the ones whose rolled-up counts stayed uncorrected.

The upstream .NET implementation, `ConsistentProbabilitySampler` in
`OpenTelemetry.Extensions`, has only ever shipped prereleases (latest `1.21.0-beta.1`). A
stable package that depends on a prerelease gets NuGet warning NU5104, and this repo stays on
stable dependency versions.

## Decision

- **`Flare.OpenTelemetry` carries its own internal `ConsistentProbabilitySampler`** and
  `TraceSampleRatio` below 1 installs `ParentBasedSampler(ConsistentProbabilitySampler(ratio))`.
  It adds no dependency.
- **It implements the spec's root decision only.** The threshold is `round((1 - p) × 2^56)`,
  written as 14 hex digits with trailing zeros dropped (`th:8` for 1/2). The randomness is
  `ot=rv:` from the parent context when present, otherwise the trace id's low 56 bits, which
  .NET generates randomly. A trace is kept when `randomness ≥ threshold`, and the kept root gets
  `ot=th:…` merged into its `tracestate` (other `ot` sub-keys and vendors kept). Child spans
  inherit the tracestate, and `ParentBasedSampler` keeps them with their root, so every span of
  a kept trace carries the same `th`.
- **It's internal and temporary.** A roadmap item tracks replacing it with the upstream
  sampler once `OpenTelemetry.Extensions` ships a stable release.

## Consequences

- Apps using `TraceSampleRatio` now get correct counts on Services, the map and SLOs (ADR-0279),
  and their decisions agree with other consistent-probability samplers on the same trace.
- Which traces are kept changes compared with `TraceIdRatioBasedSampler`, which compared a
  different slice of the trace id. The kept fraction is the same.
- The threshold isn't rounded to the spec's 4-digit default precision, so `tracestate` can
  carry up to 14 digits (`th:e6666666666668` for 10%). Ingest decodes either form to the same
  weight.
- The sampler ignores the W3C Level 2 random flag. Every .NET trace id is random, and an
  upstream sender that isn't random should provide `rv`.

## Alternatives considered

- **Depend on `OpenTelemetry.Extensions` 1.21.0-beta.1.** Less code, but it ships a stable
  package with a prerelease dependency and suppresses NU5104.
- **Wait for a stable upstream release.** Leaves the package's users with under-counted
  rollups for an unknown time, while the spec-defined root decision is small.
