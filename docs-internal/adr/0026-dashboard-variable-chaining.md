# ADR-0026: Dashboard variable chaining - resolve-time narrowing, no separate dependency store

Status: Accepted

Date: 2026-09-13

## Context

ADR-0025 shipped real, user-defined dashboard variables but explicitly left
chaining out: every `Query`-sourced variable's option list resolved
independently against the same fixed 7-day window, with no way for one
variable's options to narrow based on another's current selection. That
ADR's own "Consequences" section called this out as deliberately not
attempted ("real chaining needs a dependency graph between variables ...
this ADR doesn't attempt") and the roadmap tracked it as open work
(`docs-internal/planning/roadmap.md`'s "Custom, user-built dashboards"
item, citing SigNoz's own chained-variable work as prior art). This ADR is
that follow-up.

Two things needed deciding:

1. What does "depend on another variable" actually change about how a
   variable's options are resolved?
2. Given any number of variables, each optionally depending on any other,
   how is resolution ordered (and cycles avoided) without a fragile ad hoc
   traversal duplicated wherever variables get resolved?

## Decision

**A variable's `dependsOnVariableId` is one more optional field on
`DashboardVariable` (persisted, like every other part of its definition),
and chaining is entirely resolve-time: no new stored shape, no separate
dependency table, no explicit graph object anywhere in memory.**

- **The parent's selected value narrows the child's own wide-window
  query, not a different window.** A chained variable still resolves
  against the same 7-day window every `Query`-sourced variable always has
  (ADR-0025's own `WINDOW_MS`) - chaining adds a `services`/`attributes`
  constraint drawn from the parent's *currently selected* value onto that
  same query, the same way a Logs/Traces panel's own saved filters and a
  dashboard variable's *applied* value already combine (ADR-0025's
  "append, never replace" rule). A `Service`-target parent contributes
  `services: [value]`; an `Attribute`-target parent contributes one
  `attributes` entry, dropped silently (falling back to the unchained,
  unscoped query) rather than erroring when the parent's bag isn't
  expressible against the child's own endpoint (e.g. a `Span`-bag parent
  narrowing a Logs-endpoint child - Logs' `LogFilter` has no `Span` bag).
  A parent that's currently unselected ("All") also falls back to
  unscoped, same as if `dependsOnVariableId` weren't set at all - a
  dependency is a *when-available* narrowing, never a hard requirement to
  pick the parent first.
- **Resolution order comes from a small parent-before-child walk done at
  the two moments it's actually needed, not a maintained graph.**
  `DashboardViewerState.#loadVariableOptions()` (initial load / after
  add-edit-remove) resolves each variable through a memoizing
  `resolveOne()` that, on hitting a `dependsOnVariableId`, first resolves
  the parent (recursively, so a chain more than one level deep still
  resolves outside-in) before resolving itself - no separate topological
  sort pass, no cycle-detection data structure kept around after the call
  returns. `setVariableValue()` additionally walks *forward* -
  `#refreshDependentsOf(changedId)` re-resolves every variable whose
  `dependsOnVariableId` points at the value that just changed, then
  recurses into *their* dependents - so a selection change cascades
  through an arbitrarily deep chain without the caller needing to know
  the chain's shape up front. Both walks guard against a malformed/
  manually-edited layout's dependency cycle by tracking in-progress ids
  and treating a cycle as "no dependency" rather than recursing forever -
  the same best-effort, no-user-visible-error posture ADR-0025's own
  option resolution already has for a failed lookup.
- **A dependent variable whose current selection falls outside its
  freshly-narrowed options resets to "All", not left stale.** Same
  "off means don't touch this filter" fallback an unselected variable
  already gets - a value that's no longer among what the parent's new
  selection would actually offer stops narrowing anything rather than
  silently keep narrowing every applicable panel by a now-meaningless
  value.
- **The variable form offers only non-cycle-forming candidates.**
  `VariableFormDialog`'s "Depends on" picker excludes the variable being
  edited and anything that already (transitively) depends on it - computed
  client-side from the same `dependsOnVariableId` links, not a new
  endpoint - so a cycle can't be created through the UI at all; the
  resolve-time cycle guard above exists only as a defense against a
  layout edited some other way (direct API call, hand-edited export JSON
  reimported).

## Consequences

- No ClickHouse migration, no `Flare.Api` change - `DashboardVariable`
  gained one more optional field inside the same opaque `LayoutJson`
  blob ADR-0023 established Flare.Api never interprets.
- `removeVariable()` now also clears `dependsOnVariableId` on any variable
  that pointed at the one being removed, so a deleted parent doesn't leave
  a dangling reference behind (harmless at resolve time either way - an
  unknown parent id already resolves as "no dependency" - but left
  pointing at nothing indefinitely in the saved definition otherwise).
- A dependency is scoped to whatever the parent's bag/target actually
  supports against the child's own resolution endpoint (Logs vs. Traces) -
  there is no cross-bag translation (e.g. no way to narrow a Traces-only
  `Span`-bag variable's options by a Logs-only `Log`-bag parent's value);
  such a pairing is offered in the picker (nothing about bag compatibility
  is checked there) but silently resolves unscoped, same as picking no
  parent at all.
- Chaining depth isn't limited, but each additional level adds one more
  sequential network round trip to `#loadVariableOptions()`'s initial
  resolution (a child can't resolve before its parent does) - fine for
  the small variable counts a dashboard realistically has, not something
  this ADR optimizes further (e.g. no batched resolution).

## Related documentation

- `docs-internal/adr/0025-dashboard-variables.md` - the variable system
  this ADR adds chaining to, without changing anything else about it.
- `docs/how-to/build-custom-dashboards.md` - user-facing how-to, "Variable
  chaining" section, updated in the same PR as this ADR per this repo's
  documentation convention.
