# ADR-0240: Prometheus API: absent, label_replace and label_join

Status: Accepted

Date: 2026-10-17

## Context

[ADR-0239](0239-prometheus-api-binary-operators-offset-topk-functions.md) left `absent`, `label_replace` and
`label_join` rejected. They are among the functions Grafana panels and alert expressions lean on most:
`absent(up{job="api"})` is the standard "this service went silent" alert, and `label_replace` reshapes `instance`
into `host` so two selectors can be matched with `on(host)`. None of them depends on how Flare stores samples, so
unlike `min_over_time` there is no accuracy reason to hold them back.

## Decision

- **`absent(v)`** returns one series with value 1 at each step where `v` has no sample, and nothing where it does.
  A metric Flare has never seen is absent at every step, matching Prometheus. The result is labelled from the
  selector's equality matchers (not `__name__`), except a label that is matched more than once, as Prometheus does.
- **`label_replace(v, dst, replacement, src, regex)`** anchors the regex (`^(?:regex)$`), expands `$1`/`${1}` into
  `dst` on a match, leaves the series alone on no match, and removes `dst` when the expansion is empty.
  **`label_join(v, dst, separator, src...)`** joins the source values. Both keep `__name__`, validate label names,
  and fail with Prometheus's "same labelset" error if the rewrite makes two series identical.
  Regexes run with the same 250 ms timeout as selector matchers.
- Implemented as two AST nodes (`PromAbsent`, `PromLabelFunction`); the label logic is pure in `PromLabelFunctions`.

## Alternatives considered

- **Evaluate `absent` in the selector fetch.** Rejected: `absent` has to see the result of any instant expression
  (`absent(sum(x))`), so it runs on the evaluated vector, like the other functions.

## Consequences

- A "service went silent" alert expression and `instance` to `host` relabeling work from Grafana unchanged.
- .NET expands `$1x` as group 1 followed by `x`; Go reads it as a group named `1x`. Write `${1}x` to be portable.
- Still rejected: `@`, subqueries, `time()`, `scalar()`, `predict_linear`, `deriv`, `delta`, `changes`, `resets`,
  `sort*`, `count_values`, `min_over_time`/`max_over_time` and `__name__` regex selection.
