<script lang="ts">
	import { onMount } from 'svelte';
	import { SavedViewsState } from '$lib/saved-views/state.svelte';
	import { savedViewsContext } from '$lib/saved-views/context';
	import SavedViewTable from '$lib/components/saved-views/SavedViewTable.svelte';
	import RenameViewDialog from '$lib/components/saved-views/RenameViewDialog.svelte';

	const views = savedViewsContext.set(new SavedViewsState());

	onMount(() => {
		void views.load();
	});
</script>

<svelte:head>
	<title>Flare — Views</title>
</svelte:head>

<div class="flex h-full flex-col">
	<SavedViewTable />
</div>
<RenameViewDialog />
