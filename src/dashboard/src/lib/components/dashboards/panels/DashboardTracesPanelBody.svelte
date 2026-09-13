<script lang="ts">
	// Renders a "Traces" panel by reusing the Traces Explorer page's own state class +
	// trace list wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see
	// that file's header comment (including the `timeRangeOverride` handling, mirrored
	// here). TraceList virtualizes its rows against its container's actual height, so this
	// needs an explicit bounded-height flex column around it (DashboardPanelCard.svelte's
	// body slot provides one) rather than growing freely.
	// `refreshToken` (Phase 3) is DashboardViewerState's auto-refresh tick - see
	// DashboardLogsPanelBody.svelte's header comment for the general shape.
	import { onMount, untrack } from 'svelte';
	import { TracesExplorerState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import TraceList from '$lib/components/traces/TraceList.svelte';
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

	const explorer = tracesExplorerContext.set(new TracesExplorerState());
	let ready = $state(false);

	onMount(() => {
		explorer.applySavedViewState(query);
		ready = true;
	});

	// See DashboardLogsPanelBody.svelte's identical field for why this isn't `$state` and
	// why it's seeded from the current prop via `untrack`.
	let overrideWasActive = untrack(() => timeRangeOverride != null);

	$effect(() => {
		const override = timeRangeOverride;
		if (!ready) return;
		// untrack: see DashboardLogsPanelBody.svelte's identical effect for why this whole
		// block must be untracked - explorer.setXxx() ends up (via applyFilterChange-style
		// methods' own synchronous prefix) reading this same explorer's `filter.*` fields,
		// which would otherwise make this effect depend on state it just wrote a moment
		// earlier and loop (effect_update_depth_exceeded, caught live during e2e verification).
		untrack(() => {
			if (override) {
				explorer.setTimeRangePreset(override);
			} else if (overrideWasActive) {
				// See DashboardLogsPanelBody.svelte's identical branch for why a still-active
				// serviceOverride needs reapplying right after this wholesale revert.
				explorer.applySavedViewState(query);
				if (serviceOverride) explorer.setServices([serviceOverride]);
			}
		});
		overrideWasActive = override != null;
	});

	// See DashboardLogsPanelBody.svelte's identical field/effect pair for the dashboard-wide
	// "Service" variable - mirrored here verbatim.
	let serviceOverrideWasActive = untrack(() => serviceOverride != null);

	$effect(() => {
		const override = serviceOverride;
		if (!ready) return;
		// See the timeRangeOverride effect above for why this must be untracked.
		untrack(() => {
			if (override) {
				explorer.setServices([override]);
			} else if (serviceOverrideWasActive) {
				explorer.applySavedViewState(query);
				if (timeRangeOverride) explorer.setTimeRangePreset(timeRangeOverride);
			}
		});
		serviceOverrideWasActive = override != null;
	});

	// See DashboardLogsPanelBody.svelte's identical block for why this compares against a
	// snapshot rather than reacting to every refreshToken value unconditionally.
	let lastRefreshToken = untrack(() => refreshToken);

	$effect(() => {
		const token = refreshToken;
		if (!ready || token === lastRefreshToken) return;
		lastRefreshToken = token;
		void explorer.runSearch();
	});
</script>

<TraceList />
