<script lang="ts">
	// Renders a "Logs" panel by reusing the Logs Explorer page's own state class + volume
	// chart wholesale, exactly the way `?view=<id>` hydration on `/` does - see
	// docs-internal/adr/0023-custom-dashboards.md's "execution stays client-side,
	// unchanged" decision. Each panel instance gets its own private LogsExplorerState
	// (via a fresh context set here, scoped to this component's own subtree) - sibling
	// panels never share one, same isolation `+page.svelte` gets by construction.
	//
	// `timeRangeOverride` (Phase 2, docs-internal/adr/0024-custom-dashboards-phase2-editor.md)
	// layers the dashboard-wide time-range picker on top of the panel's own saved range -
	// see the `$effect` below for why it's gated on `ready` and tracks its own "was an
	// override active last run" flag rather than reacting to every `timeRangeOverride`
	// change unconditionally.
	//
	// `refreshToken` (Phase 3) is DashboardViewerState's auto-refresh tick, bumped once per
	// interval - this panel doesn't own a timer itself, it just re-runs its own query
	// whenever the number it's handed changes, same "state flows down, this component
	// reacts" shape as timeRangeOverride.
	import { onMount, untrack } from 'svelte';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import type { TimeRangePreset } from '$lib/logs/time-range';

	let {
		query,
		timeRangeOverride,
		serviceOverride,
		refreshToken
	}: {
		query: unknown;
		timeRangeOverride: TimeRangePreset | null;
		serviceOverride: string | null;
		refreshToken: number;
	} = $props();

	const explorer = logsExplorerContext.set(new LogsExplorerState());
	let ready = $state(false);

	onMount(() => {
		explorer.applySavedViewState(query);
		ready = true;
	});

	// Not reactive state - just remembers, across effect runs, whether an override was
	// applied last time so a later "override turned off" transition knows to restore the
	// panel's own saved range instead of leaving whatever the override last set. Seeded
	// from the current prop (via `untrack` - a deliberate one-time snapshot, not a
	// reactive read) so the very first (post-`ready`) run doesn't wastefully re-apply
	// `query` when there was never an override to begin with.
	let overrideWasActive = untrack(() => timeRangeOverride != null);

	$effect(() => {
		const override = timeRangeOverride;
		if (!ready) return; // let onMount's initial applySavedViewState land first - see its own remarks on ordering
		// untrack: explorer.setTimeRangePreset/applySavedViewState/setServices all end up
		// (via applyFilterChange -> runSearch's synchronous prefix, before its first await)
		// reading this same explorer's own `filter.*` fields to build the search request -
		// left untracked, that read would make *this* effect depend on state it just wrote
		// a moment earlier, which Svelte detects as a self-triggering loop and throws
		// effect_update_depth_exceeded for (found live during e2e verification - see the
		// serviceOverride effect below, where this was first caught). This effect must
		// depend only on timeRangeOverride/serviceOverride/ready, never on anything
		// explorer.setXxx() happens to read while doing its job.
		untrack(() => {
			if (override) {
				explorer.setTimeRangePreset(override);
			} else if (overrideWasActive) {
				// Reverting wholesale reapplies *every* saved field, including services - so a
				// still-active serviceOverride (independent of this one) needs reapplying right
				// after, or turning the time-range override off would silently also undo the
				// service override. See serviceOverride's own effect below for the symmetric case.
				explorer.applySavedViewState(query);
				if (serviceOverride) explorer.setServices([serviceOverride]);
			}
		});
		overrideWasActive = override != null;
	});

	// Same "seed via untrack, compare on the next run" shape as overrideWasActive above, for
	// the dashboard-wide "Service" variable (DashboardViewerState.serviceOverride's own
	// remarks - the MVP scope this is).
	let serviceOverrideWasActive = untrack(() => serviceOverride != null);

	$effect(() => {
		const override = serviceOverride;
		if (!ready) return;
		// See the timeRangeOverride effect above for why this whole block must be untracked -
		// this is exactly where that loop was first caught live (explorer.setServices, via
		// applyFilterChange -> runSearch's synchronous prefix, reads this.filter.services
		// right after writing it).
		untrack(() => {
			if (override) {
				explorer.setServices([override]);
			} else if (serviceOverrideWasActive) {
				// Symmetric to the time-range effect above - reapply a still-active time-range
				// override after the wholesale revert undoes it too.
				explorer.applySavedViewState(query);
				if (timeRangeOverride) explorer.setTimeRangePreset(timeRangeOverride);
			}
		});
		serviceOverrideWasActive = override != null;
	});

	// Same "seed via untrack, compare on the next run" shape as overrideWasActive above -
	// skips the spurious first fire (refreshToken starts at 0 and hasn't ticked yet) so
	// only an actual auto-refresh interval elapsing re-runs the query.
	let lastRefreshToken = untrack(() => refreshToken);

	$effect(() => {
		const token = refreshToken;
		if (!ready || token === lastRefreshToken) return;
		lastRefreshToken = token;
		void explorer.runSearch();
	});
</script>

{#if ready}
	<VolumeChart />
{:else}
	<div class="flex items-center justify-center py-8"><Spinner /></div>
{/if}
