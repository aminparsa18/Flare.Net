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
	import { onMount, untrack } from 'svelte';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import type { TimeRangePreset } from '$lib/logs/time-range';

	let { query, timeRangeOverride }: { query: unknown; timeRangeOverride: TimeRangePreset | null } = $props();

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
		if (override) {
			explorer.setTimeRangePreset(override);
		} else if (overrideWasActive) {
			explorer.applySavedViewState(query);
		}
		overrideWasActive = override != null;
	});
</script>

{#if ready}
	<VolumeChart />
{:else}
	<div class="flex items-center justify-center py-8"><Spinner /></div>
{/if}
