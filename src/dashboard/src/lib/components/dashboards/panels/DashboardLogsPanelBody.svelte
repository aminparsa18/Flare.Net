<script lang="ts">
	// Renders a "Logs" panel by reusing the Logs Explorer page's own state class + volume
	// chart wholesale, exactly the way `?view=<id>` hydration on `/` does - see
	// docs-internal/adr/0023-custom-dashboards.md's "execution stays client-side,
	// unchanged" decision. Each panel instance gets its own private LogsExplorerState
	// (via a fresh context set here, scoped to this component's own subtree) - sibling
	// panels never share one, same isolation `+page.svelte` gets by construction.
	//
	// `timeRangeOverride` (Phase 2, docs-internal/adr/0024-custom-dashboards-phase2-editor.md)
	// layers the dashboard-wide time-range picker on top of the panel's own saved range.
	// `variableOverrides` (Phase 5, docs-internal/adr/0025-dashboard-variables.md) is every
	// currently-selected dashboard variable's value, already resolved - a `Service`-target
	// variable narrows `filter.services` (Phase 4's fixed override, generalized to any
	// number of user-defined variables), an `Attribute`-target one is appended to this
	// panel's own saved `attributeFilters` (never replacing them - a panel that already
	// filters on, say, `level=error` keeps that filter alongside a dashboard variable's
	// own attribute constraint).
	//
	// Both overrides are applied by one combined effect below that recomputes the panel's
	// *entire* effective filter from its saved baseline every time either changes, rather
	// than Phase 4's pairwise "revert this one, reapply that one" dance - that only ever
	// scaled to exactly two independent overrides (time range + one fixed variable); with
	// any number of variables now possible, recomputing from scratch each time is the
	// simpler rule and the one that still can't drop an override that's still active.
	//
	// `refreshToken` (Phase 3) is DashboardViewerState's auto-refresh tick, bumped once per
	// interval - this panel doesn't own a timer itself, it just re-runs its own query
	// whenever the number it's handed changes, same "state flows down, this component
	// reacts" shape as timeRangeOverride/variableOverrides.
	//
	// VolumeChart gets `allowZoom={false}`: its built-in drag-to-zoom gesture otherwise lets
	// this one panel's fetched range silently diverge from timeRangeOverride with no visual
	// indicator, defeating "one global time range driving every panel" (roadmap design
	// note) - see VolumeChart.svelte's own remarks on the prop.
	import { onMount, untrack } from 'svelte';
	import { LogsExplorerState, type LogsSavedViewState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import { attributesForLogsPanel, type ResolvedVariableOverrides } from '$lib/dashboards/variables';

	let {
		query,
		timeRangeOverride,
		variableOverrides,
		refreshToken
	}: {
		query: unknown;
		timeRangeOverride: TimeRangePreset | null;
		variableOverrides: ResolvedVariableOverrides;
		refreshToken: number;
	} = $props();

	const explorer = logsExplorerContext.set(new LogsExplorerState());
	let ready = $state(false);

	onMount(() => {
		explorer.applySavedViewState(query);
		ready = true;
	});

	/**
	 * Re-applies this panel's saved baseline plus every currently-active override whenever
	 * either the time-range override or `variableOverrides` changes (including the initial
	 * transition to `ready`, so an override that's already active - e.g. a variable with a
	 * `defaultValue` - is reflected as soon as the panel mounts, same as Phase 4's own
	 * initial-application behavior).
	 */
	$effect(() => {
		const range = timeRangeOverride;
		const overrides = variableOverrides;
		if (!ready) return; // let onMount's initial applySavedViewState land first
		// untrack: explorer.setXxx()/applySavedViewState all end up (via applyFilterChange ->
		// runSearch's synchronous prefix, before its first await) reading this same explorer's
		// own `filter.*` fields to build the search request - left untracked, that read would
		// make *this* effect depend on state it just wrote a moment earlier, which Svelte
		// detects as a self-triggering loop and throws effect_update_depth_exceeded for
		// (found live during Phase 4's own e2e verification).
		untrack(() => {
			explorer.applySavedViewState(query);
			if (range) explorer.setTimeRangePreset(range);
			if (overrides.services.length) explorer.setServices(overrides.services);
			const attributes = attributesForLogsPanel(overrides);
			if (attributes.length) {
				const saved = (query as Partial<LogsSavedViewState> | null)?.attributeFilters ?? [];
				explorer.setAttributeFilters([...saved, ...attributes]);
			}
		});
	});

	// Same "seed via untrack, compare on the next run" shape as above - skips the spurious
	// first fire (refreshToken starts at 0 and hasn't ticked yet) so only an actual
	// auto-refresh interval elapsing re-runs the query.
	let lastRefreshToken = untrack(() => refreshToken);

	$effect(() => {
		const token = refreshToken;
		if (!ready || token === lastRefreshToken) return;
		lastRefreshToken = token;
		void explorer.runSearch();
	});
</script>

{#if ready}
	<VolumeChart allowZoom={false} />
{:else}
	<div class="flex items-center justify-center py-8"><Spinner /></div>
{/if}
