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
		refreshToken
	}: { query: unknown; timeRangeOverride: TimeRangePreset | null; refreshToken: number } = $props();

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
		if (override) {
			explorer.setTimeRangePreset(override);
		} else if (overrideWasActive) {
			explorer.applySavedViewState(query);
		}
		overrideWasActive = override != null;
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
