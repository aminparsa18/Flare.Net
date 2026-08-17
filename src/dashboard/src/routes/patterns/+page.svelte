<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { PatternsState } from '$lib/patterns/state.svelte';
	import { patternsContext } from '$lib/patterns/context';
	import PatternsToolbar from '$lib/components/patterns/PatternsToolbar.svelte';
	import PatternsTable from '$lib/components/patterns/PatternsTable.svelte';

	const patterns = patternsContext.set(new PatternsState());

	onMount(() => {
		void patterns.load();
		void patterns.loadKnownServices();
	});

	onDestroy(() => {
		patterns.dispose();
	});
</script>

<svelte:head>
	<title>Flare — Patterns</title>
</svelte:head>

<div class="flex h-full flex-col">
	<PatternsToolbar />
	<PatternsTable />
</div>
