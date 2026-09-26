# ADR-0058: Multi-value dashboard variables - OR semantics, one list-shaped selection

Status: Accepted

Date: 2026-09-26

## Context

ADR-0025's dashboard variables resolve to one value or "All"
(`defaultValue: string | null`), so a dashboard can't be scoped to, say,
two services at once. The filters a variable writes into are already
list-shaped: `LogFilter.Services`/`SpanFilter.Services` are lists, and
`AttributeFilter`/`SpanAttributeFilter` already support an `In` operator
with a `Values` operand. The roadmap tracked this as open work, with
SigNoz's multi-select variables
([signoz#5191](https://github.com/SigNoz/signoz/commit/a65d5095a0dc1aadbf6b66bae665d25ebddc8bb2))
as prior art.

Three things needed deciding:

1. How a multi-value variable is stored, and how that coexists with every
   single-value variable already saved in `LayoutJson`.
2. What several selected values mean when applied to a panel.
3. How a multi-value parent narrows a chained child (ADR-0026).

## Decision

**Multi-value is an opt-in per-variable flag, selections are always
`string[]`, and several values OR together everywhere - including when a
multi-value parent narrows a chained child.**

- **Stored shape: `multi?: boolean` plus `defaultValues?: string[]` on
  `DashboardVariable`.** Both are optional, so every variable saved before
  this ADR is read as single-value with no migration (the layout is an
  opaque JSON blob - no backend or ClickHouse change). `defaultValue` keeps
  its meaning for single-value variables; a `multi` variable uses
  `defaultValues` instead. We kept two fields rather than widening
  `defaultValue` to `string | string[]`, so that no reader has to handle a
  union type.
- **Session selection is `Record<variableId, string[]>` for every
  variable** (`[]` = "All"). A single-value variable is just a list of at
  most one value, so override resolution, chaining and revalidation have
  one code path instead of branching on `multi`. Switching a variable from
  multi back to single trims its current selection to its first value.
- **OR semantics.** A `Service` variable adds every selected value to the
  panel's `services` list, which is already an OR-list (and was already
  how several Service variables combined). An `Attribute` variable with
  one selected value still emits the same `Equals` filter as before. With
  more than one selected value it emits a single `In` filter, which the
  backend already supports for Logs and Traces. AND across the values of
  one variable was never an option: an attribute can't equal two values
  at once.
- **Chaining: a multi-value parent narrows its child by *any* of its
  selected values** - the same `services`/`In` narrowing applied to the
  child's option query. When a parent changes, the child's selection is
  pruned to values still offered rather than wholesale reset, so a
  multi-value child keeps whatever still applies (a single-value child
  still resets to "All", same as ADR-0026).
- **UI: a checkbox popover (`VariableMultiPicker.svelte`) only for `multi`
  variables**, with "All" (clear) and a per-value "Only" shortcut, and a
  filter box once there are more than a handful of options. Single-value
  variables keep the existing dropdown unchanged.

## Consequences

- No backend, API, or migration change. Older dashboards and exported
  dashboard files load unchanged.
- An older dashboard build that opens a dashboard saved with `multi` ignores
  the flag and `defaultValues`. The variable then behaves as single-value
  with no default. That is a safe degradation, not an error.
- Metrics panels are still narrowed only by `Service` variables (they have
  no attribute filter). A multi-value Service variable narrows them to all
  selected services, the same as the other panel types.
- Selections are still session-only (ADR-0025). Persisting a multi-value
  selection in the URL remains out of scope, the same as for single-value
  variables.
