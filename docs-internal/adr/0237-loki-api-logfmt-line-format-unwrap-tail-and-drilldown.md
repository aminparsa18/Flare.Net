# ADR-0237: Loki API: logfmt, line_format, unwrap, live tail and Logs Drilldown

Status: Accepted

Date: 2026-10-16

## Context

ADR-0236 gave Grafana a read-only Loki API over Flare's logs, with a deliberately small LogQL: selectors, line
filters, `| json`, label filters and the counting range functions. It left out `logfmt`, `line_format`, `unwrap`,
live tail and the endpoints Grafana's Logs Drilldown app calls, and listed them as follow-ups.

Logs Drilldown is the reason to do them together. Its queries are `{...} | json | logfmt | drop __error__,
__error_details__ | field="x" | line_format "{{.msg}}"`, its numeric field charts are `unwrap` queries, its
patterns tab calls `/patterns` and its "include pattern" button adds a `|> "..."` line filter, and it cannot
choose how to chart a field without `/detected_fields`. Live tail is the same Loki feature family and Flare
already has a live tail.

## Decision

- **`| logfmt` is a ClickHouse map built from one regular expression, shared with C#.** `LokiLogfmt.PairPattern`
  (RE2 syntax, also valid for .NET) defines a `key=value` pair: a bare word or a double-quoted string with `\"`
  and `\\` escapes. ClickHouse runs it with `extractAllGroups` into a `Map(String, String)` called `lkv`, defined
  once in a `WITH` clause at the top of the query, and a logfmt label is `lkv[key]`; the response side
  (`LokiEntryLabels`, `detected_fields`, live tail) runs the same pattern in C#. A filter and the labels shown
  for the same line therefore cannot disagree. Keys are sanitised to `[a-zA-Z0-9_]`, an empty value is not a
  label, and a line that is a JSON object has no logfmt labels (Loki's decoder rejects its quotes). The flags
  `--strict` and `--keep-empty` are rejected by name.
- **Several parser stages compose.** Drilldown's "mixed" pipeline is `| json | logfmt`. `LokiSqlContext` keeps
  the stages in order and a label that no fixed name or attribute claims becomes a `multiIf` over the stages,
  latest first, each contributing its own condition (`JSONHas(...)`, `mapContains(lkv, ...)`); `level` falls
  back to the severity. `__error__` is a JSON parse error exactly when a `json` stage ran. In C#, stages run in
  order and a label that collides with an earlier one gets Loki's `_extracted` suffix.
- **`| line_format` is presentational.** The template is applied in C# to the lines that are returned, after
  the query has chosen them, so it also works as the last stage. The cost: a stage that reads the line after
  `line_format` (a line filter, a parser, a second `line_format`) would see the original text where Loki sees
  the rewritten one, so the parser rejects it, naming the rule; label filters may follow. `bytes_over_time` and
  `bytes_rate` over a `line_format` are an error for the same reason, `count_over_time` and `rate` ignore it.
  The template language is a Go `text/template` subset (`LokiTemplate`): `{{.label}}`, string and number
  literals, `__line__`, `__timestamp__`, pipelines, and `lower`, `upper`, `title`, `trim`, `trimAll`,
  `trimPrefix`, `trimSuffix`, `replace`, `trunc`, `default`. Control actions and unknown functions are rejected
  when the query is parsed. A template sees parsed labels first, then structured metadata, then the stream.
- **`| unwrap` with the `*_over_time` functions.** `unwrap label`, `unwrap duration(label)` (also
  `duration_seconds`) and `unwrap bytes(label)` become a nullable number in SQL (Go durations and sizes are
  parsed with regular expressions), and a line whose value is not a number is not a sample. `sum`, `avg`, `min`,
  `max`, `first`, `last`, `stddev` and `stdvar` are mergeable, so one read returns per
  (bucket, labels) the count, sum, min, max, first and last value, mean and sum of squared deviations
  (`LokiBucketStats`), and the evaluator merges the buckets of each window, with Chan's formula for variance.
  `quantile_over_time` is not mergeable, so ClickHouse computes it per evaluation step: each row is expanded
  with `arrayJoin` to the windows that contain it and `quantileExactInclusive` gives Prometheus's
  interpolation. A row falls in about `range / step` windows, so a window wider than 120 steps is an error
  asking for a larger step, as is any evaluation that would merge more than 30 million buckets. The function's
  own `by`/`without` groups the samples before the function applies, as in Loki. A `sum by` around
  `sum_over_time` is pushed into ClickHouse (a sum of sums is the sum); around anything else it is not.
  `rate_counter` and `absent_over_time` are rejected by name.
- **Pattern line filters, `|>` and `!>`,** compile to one regular expression (`LokiPatternMatcher`): a leading
  literal must start the line, the literals must follow in order, `<_>` is any text, named captures are rejected.
  This is what Drilldown's patterns tab adds to the query.
- **Live tail is the existing broadcaster plus an in-memory evaluator.** `GET /loki/api/v1/tail` upgrades to a
  WebSocket, subscribes to `LogTailBroadcaster`, and runs every stage over each log in `LokiTailMatcher`, which
  builds on the same label code as a response, so a tailed line has the labels and `line_format` output a query
  would give it. Equality on `service_name` and `detected_level` is also given to the broadcaster as an
  ordinary `LogFilter`, so the shared fan-out loop does not queue what the connection would discard; the rest
  runs on the connection's own send loop. Regular expressions run in .NET's non-backtracking engine with live
  tail's existing limits (count, length, timeout). A query it cannot evaluate faithfully (a lookaround or
  backreference, or a metric query) is a 400 naming the pattern, never approximated. `start` (optional) first
  sends up to `limit` older entries from the table, oldest first; without it only new entries arrive. The
  origin check is skipped for a request that carries an `Authorization` header, because a browser cannot set
  one on a WebSocket, so it cannot be another site riding a visitor's cookies; Grafana forwards its own origin.
  After `LiveTail:MaxSocketLifetime` the connection is dropped without a close frame, which Grafana's live view
  treats as a lost connection and re-opens, re-authorizing it.
- **Logs Drilldown endpoints.**
  - `detected_labels`: the stream labels (`service_name` and the indexed resource attributes) present in the
    window, each with `uniq` of its values.
  - `detected_fields`: the newest `line_limit` (1000) lines the query matches are read and analysed in C#
    (`LokiDetectedFields`): structured metadata (no parser), `| json` fields (with their key path, nested keys
    joined by `_`) and `| logfmt` fields, each with a guessed type (`boolean`, `int`, `float`, `duration`,
    `bytes`, `string`) and a distinct-value count capped at 5000. A parsed name that collides with metadata is
    `name_extracted`. `detected_field/{name}/values` runs the field through both parsers when the query has none.
  - `patterns`: Drain clusters (ADR-0007) as counts per `step`, one series per cluster and level, because
    Drilldown filters patterns by level. `<*>` becomes Loki's `<_>`. The template of a cluster is the latest
    one seen, since Drain generalises it as lines arrive. At most `limit` (100) clusters, busiest first.
  - `index/volume`: bytes and entries per combination of `targetLabels`, or, as in Loki, of the labels the
    selector names.
  - `status/buildinfo` now reports 3.5.0, a version Drilldown accepts.

Verified end to end against a real ClickHouse and Grafana 13.2.4 with Logs Drilldown 3.0.0: the service
selection page, the overview (log volume and lines), the Labels, Fields (including `unwrap duration(...)` and
`unwrap` charts) and Patterns tabs; every unwrapped function against the same statistics computed directly in
ClickHouse; and live tail from a WebSocket client, directly and through Grafana's data source proxy.

Drilldown writes `| json` expressions with bracket paths (`request_method="[\"request\"][\"method\"]"`), so
`| json label="path"` accepts dotted paths, quoted bracket keys and array indexes (`LokiJsonPath`).

## Alternatives considered

- **Translate `logfmt` to `extractKeyValuePairs`.** Rejected: its quoting and escaping rules would differ from
  the C# parser, and a filter and a displayed label could disagree.
- **Apply `line_format` in SQL.** Rejected: a Go template cannot be compiled to ClickHouse expressions without
  re-implementing the template engine there. Rejecting the stages that would read the rewritten line keeps the
  result exact.
- **Evaluate live tail with `LogFilter`.** Rejected, for the reason ADR-0236 gives: it has no negation, regex
  on the body, case-sensitive contains, or parsed labels. It is used only as a coarse prefilter.
- **Merge a quantile from per-bucket sketches in C#.** Rejected: an exact quantile is what a user compares with
  the data, and ClickHouse can compute it per step at a bounded cost.
- **A per-line `detected_fields` count in SQL.** Rejected: JSON and logfmt field names are open-ended, and
  Drilldown needs a type and a parser per field; sampling lines and analysing them is simple and bounded.

## Consequences

- Logs Drilldown's service selection, overview, fields and patterns tabs work against Flare, and `unwrap`
  panels chart numeric fields.
- A window wider than 120 steps cannot take a quantile, and an unwrapped function over a very long range at a
  small step is an error rather than a slow query; both ask for a larger step.
- A non-numeric unwrapped value is silently not a sample; Loki keeps it with an `__error__` label.
- `detected_fields` describes the newest sample of lines, so a field that is rare or old is not listed, as in Loki.
- Live tail evaluates regular expressions with .NET's engine, not RE2, so a few spellings behave differently
  from the same query over history; lookarounds and backreferences are refused rather than approximated.
- A slow tail client skips entries it cannot keep up with (the channel is bounded); the `droppedEntries`
  report of Loki is not sent.
