# ADR-0037: Formula-mode metrics as a dashboard panel, via a second render path

Status: Accepted

Date: 2026-09-22

## Context

ADR-0036 shipped cross-query Formula mode (named queries `A`, `B`, ...
combined by an expression, joined app-side by matching service+attributes+
timestamp) on the Metrics Explorer page only. It deliberately deferred two
dashboard-panel paths, both blocked by the same root cause:
`DashboardMetricsPanelBody.svelte` reused `MetricChart.svelte` wholesale,
which has no Formula-mode rendering.

1. `PinToDashboardButton` was hidden on `MetricsToolbar.svelte` while in
   Formula mode - pinning would have produced a panel that silently renders
   nothing.
2. `AddPanelMetricsForm.svelte` (the dashboard editor's "Add panel" flow) had
   no Formula mode at all.

ADR-0036's own Consequences section named the open question explicitly:
"whoever picks this up next will need to decide whether
`DashboardMetricsPanelBody` grows a second rendering path or a new panel
type is introduced."

## Decision

**A second rendering path on the existing `Metrics` panel type, not a new
panel type.** `DashboardMetricsPanelBody.svelte` now branches on
`explorer.mode` (restored by the existing `applySavedViewState`, unchanged
by this ADR) and renders `FormulaChart` for `'formula'`, `MetricChart` for
`'single'` - the exact same split `routes/metrics/+page.svelte` already uses
between `FormulaBuilder`+`FormulaChart` and `MetricPicker`+`MetricChart`. A
new panel type would have meant a second `DashboardPanel.panelType` value,
a second entry in `AddPanelDialog.svelte`'s type picker, and a second query
schema to keep in sync with `MetricsSavedViewState` - all to represent what
is, from the dashboard's point of view, still just "a Metrics panel," the
same way Formula mode is still just "the Metrics Explorer page" and not a
separate route.

Both dashboard-panel entry points are unblocked, not just pinning:

- **Pin-from-Explorer**: `PinToDashboardButton` is no longer gated on
  `mode === 'single'` in `MetricsToolbar.svelte`. Its `defaultTitle` now
  falls back to the formula expression itself (`explorer.formulaExpression`)
  in Formula mode, since `explorer.selected?.metricName` has no meaning
  there.
- **Add-panel-from-scratch**: `AddPanelMetricsForm.svelte` gained the same
  mode `Select` `MetricsToolbar.svelte` already has, branching its own body
  between `FormulaBuilder`+`FormulaChart` and `MetricPicker`+`MetricChart`.
  Its `valid` prop (gating the dialog's submit button) now has a
  Formula-mode definition - no parse error, and at least one referenced
  query row has a metric selected - mirroring the same guard
  `MetricsExplorerState.runFormulaQuery` already applies before fetching.
  `currentState()` needed no change; `toSavedViewState()` already
  serializes whichever mode is active.
- `AddPanelDialog.svelte`'s dialog widened from `sm:max-w-2xl` to
  `sm:max-w-4xl` so `FormulaBuilder`'s fixed `w-[420px]` query-row column
  has room alongside a usable chart preview; still comfortably fits the
  simpler Logs/Traces forms and single-metric Metrics' narrower
  `MetricPicker`.

**`FormulaChart.svelte` gained `yAxisMin`/`yAxisMax` props**, mirroring
`MetricChart.svelte`'s existing soft-bound `domainMin`/`domainMax` pattern
(the "soft Y-axis min/max on Metrics panels" feature) exactly - the bound
only ever widens the "nice" axis domain outward, never clips data past it.
Without this, a Formula panel would have silently ignored a
`DashboardPanel.yAxisMin`/`yAxisMax` a user configured on it, the same
"looks configurable, silently does nothing" trap ADR-0036 avoided elsewhere.
The Explorer page never passes these props, so `FormulaChart`'s own
behavior there is unchanged.

No backend changes. No new panel-type/query-shape surface - `query: unknown`
on `DashboardPanel` and `MetricsSavedViewState`'s already-shipped
`mode`/`formulaExpression`/`formulaQueries` fields round-trip through both
entry points unchanged.

**Found and fixed one real, pre-existing bug while verifying the
`yAxisMin`/`yAxisMax` addition above**: `YAxisBoundsPopover.svelte`'s
`apply()` called `minDraft.trim()`/`maxDraft.trim()` unconditionally, but
Svelte's `bind:value` on an `<input type="number">` coerces the bound value
to an actual `number` once the user edits it (only the initial
`String(yAxisMin)`/`String(yAxisMax)` reseed is ever a string) - so typing
into either field threw `TypeError: ....trim is not a function` and the
apply silently no-opped, for *any* Metrics panel, not just a Formula one.
Reproduced with both `fill()` and real keystrokes during this ADR's own
live verification. Fixed by typing `minDraft`/`maxDraft` as
`$state<string | number>` and dropping the `.trim()` call (`=== ''` alone
is sufficient - a number input's value never carries whitespace).

## Consequences

- `DashboardMetricsPanelBody.svelte`'s `refreshToken` effect now branches
  between `runFormulaQuery()`/`runQuery()` by `explorer.mode` - the one call
  site that called `runQuery()` directly rather than going through
  `applySavedViewState` (which already branched internally).
- Histogram-metric formula operands remain unsupported (ADR-0036's own v1
  scope cut) - unaffected by this ADR, still a separate, named follow-up.
- `AddPanelDialog.svelte`'s wider `sm:max-w-4xl` applies to all three panel
  types' add-panel forms, not just Metrics' Formula tab - a deliberate,
  low-risk side effect rather than a mode-conditional dialog width.
- The `YAxisBoundsPopover.svelte` fix applies to every Metrics panel's
  Y-axis editor, single-metric or formula - the bug predates this ADR and
  was unrelated to Formula mode, just caught by this ADR's own live
  verification of the `yAxisMin`/`yAxisMax` addition above.

## Related documentation

- `docs-internal/adr/0036-cross-query-metric-formulas.md` - the ADR this
  one completes the deferred dashboard-panel scope of.
- `src/dashboard/src/lib/components/dashboards/panels/DashboardMetricsPanelBody.svelte`
- `src/dashboard/src/lib/components/dashboards/add-panel/AddPanelMetricsForm.svelte`
- `src/dashboard/src/lib/components/metrics/FormulaChart.svelte` -
  `yAxisMin`/`yAxisMax` props.
- `src/dashboard/src/lib/components/dashboards/YAxisBoundsPopover.svelte` -
  the `minDraft`/`maxDraft` type fix.
- `src/dashboard/src/lib/components/metrics/MetricsToolbar.svelte` -
  unconditional `PinToDashboardButton`.
