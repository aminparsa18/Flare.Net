<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { TracesExplorerState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import TracesToolbar from '$lib/components/traces/TracesToolbar.svelte';
	import TraceList from '$lib/components/traces/TraceList.svelte';

	const explorer = tracesExplorerContext.set(new TracesExplorerState());

	onMount(() => {
		void explorer.runSearch();
		void explorer.loadKnownServices();
	});

	onDestroy(() => {
		explorer.dispose();
	});
</script>

<svelte:head>
	<title>Flare — Traces</title>
</svelte:head>

<div class="flex h-full flex-col">
	<TracesToolbar />
	<div class="flex min-h-0 flex-1 flex-col">
		<TraceList />
	</div>
</div>
