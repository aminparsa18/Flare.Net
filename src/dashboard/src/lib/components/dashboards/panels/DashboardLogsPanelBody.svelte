<script lang="ts">
	// Renders a "Logs" panel by reusing the Logs Explorer page's own state class + volume
	// chart wholesale, exactly the way `?view=<id>` hydration on `/` does - see
	// docs-internal/adr/0023-custom-dashboards.md's "execution stays client-side,
	// unchanged" decision. Each panel instance gets its own private LogsExplorerState
	// (via a fresh context set here, scoped to this component's own subtree) - sibling
	// panels never share one, same isolation `+page.svelte` gets by construction.
	import { onMount } from 'svelte';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import { Spinner } from '$lib/components/ui/spinner';

	let { query }: { query: unknown } = $props();

	const explorer = logsExplorerContext.set(new LogsExplorerState());
	let ready = $state(false);

	onMount(() => {
		explorer.applySavedViewState(query);
		ready = true;
	});
</script>

{#if ready}
	<VolumeChart />
{:else}
	<div class="flex items-center justify-center py-8"><Spinner /></div>
{/if}
