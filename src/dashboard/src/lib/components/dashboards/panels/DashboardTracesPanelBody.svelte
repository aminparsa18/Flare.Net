<script lang="ts">
	// Renders a "Traces" panel by reusing the Traces Explorer page's own state class +
	// trace list wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see
	// that file's header comment (including the combined `timeRangeOverride`/
	// `variableOverrides` effect, mirrored here). TraceList virtualizes its rows against
	// its container's actual height, so this needs an explicit bounded-height flex column
	// around it (DashboardPanelCard.svelte's body slot provides one) rather than growing
	// freely.
	// `refreshToken` (Phase 3) is DashboardViewerState's auto-refresh tick - see
	// DashboardLogsPanelBody.svelte's header comment for the general shape.
	import { onMount, untrack } from 'svelte';
	import { TracesExplorerState, type TracesSavedViewState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import TraceList from '$lib/components/traces/TraceList.svelte';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import { attributesForTracesPanel, type ResolvedVariableOverrides } from '$lib/dashboards/variables';

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

	const explorer = tracesExplorerContext.set(new TracesExplorerState());
	let ready = $state(false);

	onMount(() => {
		explorer.applySavedViewState(query);
		ready = true;
	});

	// See DashboardLogsPanelBody.svelte's identical effect for why this recomputes the
	// panel's whole effective filter from its saved baseline on every change instead of
	// reverting/reapplying overrides pairwise, and why the block below must be untracked.
	$effect(() => {
		const range = timeRangeOverride;
		const overrides = variableOverrides;
		if (!ready) return;
		untrack(() => {
			explorer.applySavedViewState(query);
			if (range) explorer.setTimeRangePreset(range);
			if (overrides.services.length) explorer.setServices(overrides.services);
			const attributes = attributesForTracesPanel(overrides);
			if (attributes.length) {
				const saved = (query as Partial<TracesSavedViewState> | null)?.attributeFilters ?? [];
				explorer.setAttributeFilters([...saved, ...attributes]);
			}
		});
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
