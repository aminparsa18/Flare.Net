# ADR-0238: Loki API: rate_counter, absent_over_time, logfmt --strict, unwrap errors and dropped tail entries

Status: Accepted

Date: 2026-10-17

## Context

ADR-0237 closed the large gaps in the Loki-compatible API and listed what it left out: `rate_counter`,
`absent_over_time`, `logfmt --strict` and `--keep-empty`, the `__error__` label of an `unwrap` that cannot convert a
value, and the `dropped_entries` report of live tail. Each is a place where a query that Grafana or a LogQL author
writes either fails with "not supported" or, worse for `unwrap`, quietly answers differently from Loki.

Loki's own source settled what "the same" means for each; the points that were not obvious are recorded below.

## Decision

- **`rate_counter(... | unwrap label [range])`** is the per-second rate of a counter, with Prometheus's
  `extrapolatedRate` as Loki applies it: the increase from the first to the last sample plus the value a counter
  fell from at each reset, stretched towards the window edges when the first or last sample is within 1.1 times the
  average spacing of them, never extrapolated below a zero counter, and 0 for fewer than two samples. It is
  mergeable, so one read returns per (bucket, labels) what the formula needs (`LokiCounterStats`): the sample count,
  the time and value of the first and last sample, and the reset correction. ClickHouse orders each bucket's
  samples (`arraySort` over `groupArray((time, value))`) and sums the drops; the evaluator joins the buckets of a
  window, adding the drop across each join (`LokiCounterStats.Then`). The cost is that a bucket holds its samples
  in memory, so a very busy counter over a very wide bucket is bounded by the query's memory cap. It takes the
  function's own `by`/`without`, and is not pushed down through `sum by`.
- **`absent_over_time({...} [range])`** reads the same counts as `count_over_time` with no label dimensions and
  returns one series of 1s at the steps whose window holds no matching line, none at all when every window has one
  (as Prometheus and Loki do). The series' labels are the selector's equality matchers, minus any name that also has
  a matcher of another kind, and minus empty values (`LokiEvaluator.AbsentLabels`, a port of Loki's
  `absentLabels`). It accepts a pipeline, so `absent_over_time({job="x"} |= "heartbeat" [5m])` works.
- **`| logfmt --strict`** follows Loki's decoder. A line is well formed when it is whole items, an item being `key`,
  `key=`, `key=bare` or `key="quoted"` (a bare value contains no `=` or `"`; a quoted one has only the escapes Go
  accepts; a quoted value may be followed directly by the next key). A malformed line keeps the labels read before
  the first fault and gets `__error__="LogfmtParserErr"`. Three regular expressions in RE2 syntax define it
  (`LokiLogfmt.StrictItemPattern`, `StrictPrefixPattern`, `StrictValidPattern`) and ClickHouse and C# both run
  them: `lkb` is the well-formed prefix, `lkv` the pair map read from it, `lkok` whether the whole line matched.
  `__error__` is then the error of the latest failing parser stage (`json` on a body that is not JSON, strict
  `logfmt` on a malformed one), so a pipeline of both reads the right one. Stages that disagree about `--strict`
  are rejected, because there is one `lkv` per query. The error text of `__error_details__` is not reproduced.
- **`--keep-empty` is accepted and changes nothing.** It keeps a label whose value is empty, and Loki treats an empty
  label as an absent one in every construct Flare supports (matchers, filters, grouping, `line_format`), so there
  is nothing to observe. It is parsed so queries Grafana generates do not fail.
- **`unwrap` conversion errors follow Loki's pipeline-error rule.** A line without the label is not a sample. A line
  whose value does not convert (`unwrap latency` on `"abc"`, `duration()` on `"soon"`) is a sample of 0 with
  `__error__="SampleExtractionErr"`, and what happens next depends on the pipeline, exactly as in Loki:
  a label filter after the `unwrap` that rejects it (`| __error__=""`, which is what Logs Drilldown writes) removes
  it; a filter that accepts it (`| __error__!=""`, `| __error__="SampleExtractionErr"`, or any filter on
  `__error__` that accepts a non-empty error value, anywhere in the pipeline) keeps it as a series of its own
  labelled with the error; `| drop __error__` after the `unwrap` clears it, leaving the 0 in the series it
  belongs to; and a query that does none of these **fails** with Loki's message
  (`pipeline error: 'SampleExtractionErr' for resulting series: '{...}'. Use a label filter to intentionally skip this
  error`). This replaces the silent drop of ADR-0237. `__error__` is always the last label dimension of an
  unwrapped read and `LokiPipelineErrors.KeepsErroredLines` decides between keeping and failing. Loki also puts
  `__error_details__` (the conversion error text) in the series labels; Flare does not, so all the conversion
  errors of a series are one series.
- **Live tail reports dropped entries.** The tail frame gains `dropped_entries`, a list of
  `{"labels": {...}, "timestamp": "<ns>"}`, when the connection fell behind. A subscription now remembers up to
  100 of the events its full channel discarded (`LogTailSubscription.DroppedSampleLimit`), the send loop runs the
  query over them and reports those it would have matched. Only a sample is kept, so a burst that loses more than
  100 events reports 100.
- **A latent counting bug in live tail is fixed on the way.** `LogTailSubscription` counted the events a full channel
  discarded by checking the result of `TryWrite`, which returns `true` for a dropped item in
  `BoundedChannelFullMode.DropWrite`; the count was always 0, so the dashboard live tail never showed its
  "events dropped" notice. The drop is now observed through the channel's `itemDropped` callback.

Verified end to end against a real ClickHouse and Grafana 13.2 with Logs Drilldown 3.0.0:

- `rate_counter` equals Prometheus's extrapolated rate recomputed in ClickHouse (window functions for the resets, the
  formula in SQL) at every step of ranges of 20 s to 5 m and steps of 20 s to 100 s over a counter with a reset and
  two unconvertible values. The one disagreement is a window with a single sample, where the SQL divides by zero and
  Loki's rule gives 0.
- `absent_over_time` returns exactly the steps whose window `count()` is 0 in ClickHouse, for four range and step
  combinations and for instant queries.
- `sum`, `avg`, `min`, `max`, `first`, `last`, `stddev`, `stdvar` and `quantile_over_time` over `unwrap` still equal the
  same statistics computed in ClickHouse (99 points), now with the `__error__` dimension and the error filter.
- `logfmt --strict`: a Python port of Loki's `logfmt.Decoder` over 500 generated lines (176 faulty) agrees with
  Flare on which lines are faulty on both the SQL side (an `__error__` filter) and the C# side (labels of a
  response), and on the labels of the 114 lines that have no repeated key and no key glued to a quoted value.
- Logs Drilldown's service list, logs overview, Fields (including a numeric field with unconvertible values),
  Patterns tabs and Grafana's `/api/ds/query` load without error; a slow tail client with a one-slot channel
  received `dropped_entries` frames, and a normal one received none.

The label extraction under `--strict` is the lenient parser's (ADR-0237), so its known differences from Loki apply
unchanged: a repeated key in one line is first-wins in SQL and gets an `_extracted` suffix in a response (Loki keeps
the last), a key written directly after a quoted value (`a="x"b=1`) is not read as a label although `--strict` accepts
the line, and a bare value containing `=` is read as a value where Loki's decoder faults.

## Alternatives considered

- **Keep dropping non-numeric `unwrap` values silently.** Rejected: it answers a query Loki rejects, hides data
  problems, and is the only supported construct that still differed from Loki by dropping data instead of saying so.
  Logs Drilldown and the `| __error__=""` convention keep working.
- **Model `__error_details__` as a series label.** Rejected: every distinct bad value would be its own series in
  Loki; reproducing Go's error strings in SQL is not worth the fidelity, and `__error__` alone identifies the error.
- **Compute the counter-reset correction in C# from raw samples.** Rejected: it would move every sample of the window
  out of ClickHouse; ordering within a bucket and merging bucket statistics keeps the read small.
- **Parse logfmt `--strict` in C# and send flags to ClickHouse per line.** Rejected for the reason of ADR-0237: a
  filter and the labels shown for the same line must come from one definition.

## Consequences

- `rate_counter` and `absent_over_time` work in Grafana panels and alert expressions; `logfmt --strict` pipelines
  produce `LogfmtParserErr` series that `| __error__=""` removes.
- An `unwrap` query over a field that is sometimes not a number now fails unless it filters on `__error__`, as in
  Loki. A saved Grafana panel that relied on Flare's lenient behaviour needs `| __error__=""` after the `unwrap`.
- A `| json` or `| logfmt` pipeline in a metric query still does not fail on a parser error the way Loki does for
  a query that never mentions `__error__`; only conversion errors of `unwrap` follow the rule. Parser errors stay
  visible to filters and in the returned labels.
- `logfmt --strict` accepts a bare key (`flag`) as well formed but gives it no label; `\n` and other escapes inside a
  quoted value are valid but kept undecoded, as for the lenient parser.
- Dashboards that use live tail get the "events dropped" notice they were always meant to.
