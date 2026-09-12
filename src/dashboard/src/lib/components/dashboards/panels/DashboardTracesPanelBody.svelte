<script lang="ts">
	// Renders a "Traces" panel by reusing the Traces Explorer page's own state class +
	// trace list wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see
	// that file's header comment. TraceList virtualizes its rows against its container's
	// actual height, so this needs an explicit bounded-height flex column around it
	// (DashboardPanelCard.svelte's body slot provides one) rather than growing freely.
	import { onMount } from 'svelte';
	import { TracesExplorerState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import TraceList from '$lib/components/traces/TraceList.svelte';

	let { query }: { query: unknown } = $props();

	const explorer = tracesExplorerContext.set(new TracesExplorerState());

	onMount(() => {
		explorer.applySavedViewState(query);
	});
</script>

<TraceList />
