# ADR-0048: Anomaly-detection alerting as a fourth `AlertConditionKind`

Status: Accepted

Date: 2026-09-24

## Context

Every existing alert condition compares the current window against a fixed
number: `LogCount`/`ExceptionCount` against `AlertThreshold.Count`,
`MetricThreshold` against `MetricThresholdValue`. A fixed number can't catch
"traffic is half what it normally is at this hour". For a series with a daily
or weekly rhythm, any threshold low enough to catch a 3am outage misses the
same drop at 3pm. Any threshold high enough for 3pm then fires every night.

Prior art: SigNoz added an anomaly rule type
([signoz#5973](https://github.com/SigNoz/signoz/commit/419d2da363dd01f66b5c24f0e78ff863605b9a2a))
that scores the current value against a seasonal baseline, with UI in
[signoz#5916](https://github.com/SigNoz/signoz/commit/21801180949459200d5b471d822e5ad08a75215f).

## Decision

**A fourth `AlertConditionKind`, `Anomaly`.** It follows the
discriminator-plus-additive-migration pattern of ADR-0020 and ADR-0022. The
rule fires when the current window's value is more than *k* standard
deviations from the same window at the same time of day (or week) over the
last *N* days (or weeks).

### The series is an existing condition, not a new one

`AnomalyCondition.Source` names which existing condition kind produces the
series: `LogCount`, `MetricThreshold` or `ExceptionCount`. The series itself
comes from the rule's existing `Condition`, `MetricCondition` or
`ExceptionCondition` field. The same `IAlertQueryService` calls evaluate it
(`CountMatchingLogsAsync`, `EvaluateMetricConditionAsync`,
`CountMatchingExceptionsAsync`). So an anomaly rule has no new filter DSL, no
new query builder and no new SQL. It asks the same question as the
equivalent threshold rule, just more than once.

`AnomalyCondition` holds only the scoring parameters:

- `Seasonality`: `Daily` (period = 24h) or `Weekly` (period = 7d).
- `BaselinePeriods` *N*, 3 to 12: how many past periods to sample.
- `ZScoreThreshold` *k*, greater than 0 and at most 10: how many standard
  deviations counts as anomalous.
- `Direction`: `Above`, `Below` or `Both`.

`AlertThreshold`/`MetricThresholdValue` are ignored for `Anomaly` rules,
following the same "field present, meaningful only for one mode" convention
the other kinds use.

### Scoring

On each evaluation, with window *W* ending at `now`:

1. **Current value**: the source series over `[now - W, now]`.
2. **Baseline samples**: the same series over
   `[now - i·P - W, now - i·P]` for *i* = 1..*N*, where *P* is the
   seasonal period.
3. **Missing samples are dropped.** A sample is missing if it is `NaN` (a
   metric window with no points) or exactly `0`. Zero is ambiguous for a
   baseline: it can mean a real quiet window, or a window before the source
   existed or after retention dropped it. Treating it as a real zero would
   make every new rule fire the moment its source starts emitting, against an
   all-zero "history". This only applies to *baseline* samples. A current
   value of 0 is kept, because "traffic dropped to zero" is the main thing
   this feature exists to catch.
4. **Minimum history**: at least `AnomalyScoring.MinBaselineSamples` (3)
   samples must remain. Otherwise the rule does not fire. With fewer than
   3 samples a standard deviation says nothing useful.
5. **z-score**: `z = (current - mean) / σ_eff`, with the population standard
   deviation σ and `σ_eff = max(σ, 0.05·|mean|)`. The 5% floor matters for
   very stable series. Without it, a baseline that happened to be nearly
   constant has σ ≈ 0 and a 1% wobble scores z = 50. With the floor,
   `k = 3` needs at least a 15% deviation from the mean.
6. **Breach**: `z ≥ k` for `Above`, `z ≤ -k` for `Below`, `|z| ≥ k` for
   `Both`. A `NaN` current value (the metric reported nothing) never breaches.
   Absent-data alerting (ADR-0045) is how a rule catches that.

The pure scoring lives in `AnomalyScoring.Score`. `AnomalyEvaluator` runs
the queries and is shared by `AlertEvaluationWorker` and the `/test` dry-run
endpoints, the same arrangement as `AlertNoDataEvaluator`. This keeps the
dry run and the worker in agreement.

### Validation

`AlertRuleRequest.ValidateCondition` requires `AnomalyCondition` and the
condition for its `Source` (`Condition` is always present, `MetricCondition`
or `ExceptionCondition` otherwise). It also requires `WindowSeconds` to be
shorter than the seasonal period, so the current window never overlaps the
first baseline window. `NoDataWindowSeconds` composes with an anomaly rule
exactly as with the underlying kind: allowed for `LogCount`/`MetricThreshold`
sources, rejected for `ExceptionCount`.

### Storage

Migration `0028_alert_anomaly.sql` (plus the cluster variant) is additive only:

- `alert_rules.AnomalyConditionJson`: an opaque JSON `AnomalyCondition`,
  the same shape as `MetricConditionJson`/`ExceptionConditionJson`.
- `alert_events.BaselineMean`, `alert_events.ZScore`
  (`Nullable(Float64)`): set only for anomaly fires. `ObservedValue` holds
  the current value for every source, including counts, so history can show
  "observed 412 vs usual 1,030 (z = -4.1)" without a second event shape.

### Notifications

Notifiers get an optional `AnomalyScore`. The text reads
"Alert "X" fired: anomaly - 412 events in the last 300s vs a usual 1,030
(z = -4.1, same window over the previous 7 days)". The webhook and PagerDuty
payloads carry `baselineMean`, `zScore` and `baselineSamples`. A
`LogCount`-sourced anomaly still gets the scoped "Matching logs" link.

## Alternatives considered

- **Compute the baseline in one ClickHouse query** (`countIf` per shifted
  range). This works for log and exception counts. It does not work for
  metric sources: reset-aware `increase()` (ADR-0044) and the histogram
  quantile estimate are computed per window, and folding *N+1* windows into
  one statement would mean a second copy of every metric aggregation.
  Rejected for now. *N+1* reuses of the existing, capped queries are simple
  and always agree with the equivalent threshold rule. The cost is bounded
  (*N* ≤ 12). `EvaluationIntervalSeconds` (ADR-0046) is the knob for
  expensive rules.
- **A stored/rolled-up baseline** (a materialized per-hour history table).
  Rejected. It needs a new table, a backfill story, and a second place where
  a rule's filter is evaluated. On-demand shifted windows need no state and
  use whatever retention the source already has.
- **Median/MAD instead of mean/σ.** More robust to one bad baseline day, but
  harder to explain in a notification. With *N* ≤ 12, one outlier moves the
  mean noticeably. It also inflates σ, which makes the rule *less* sensitive,
  not noisier. Mean/σ is kept and can be revisited if false negatives show up.
- **Keep zero baseline samples.** Rejected as described in Scoring step 3.
  For sparse series that are really zero most of the time, z-scores are the
  wrong tool anyway (counts that small are Poisson, not normal). A plain
  `LogCount`/`ExceptionCount` threshold fits those better.

## Consequences

- An anomaly rule runs *N+1* condition queries per evaluation (up to 13),
  each under the same `EvaluationSafetyOptions` caps as every other alert
  query. The baseline windows move with `now`, so ADR-0029's result cache
  would not help even if alert queries went through it.
- The baseline needs the source's data to still exist *N* periods back. A
  weekly rule with *N* = 4 needs four weeks of retention. With less, it stays
  quiet (insufficient history), which is the safe failure.
- `AlertTestResult` gains `BaselineMean`, `ZScore` and `BaselineSampleCount`,
  so the form's dry run and `flare alerts test` can show why a rule would or
  wouldn't fire.
