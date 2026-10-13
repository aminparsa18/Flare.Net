<script lang="ts">
	import { onMount } from 'svelte';
	import { SavedViewsState } from '#lib/saved-views/state.svelte.ts';
	import { savedViewsContext } from '#lib/saved-views/context.ts';
	import SavedViewTable from '#lib/components/saved-views/SavedViewTable.svelte';
	import RenameViewDialog from '#lib/components/saved-views/RenameViewDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';

	const views = savedViewsContext.set(new SavedViewsState());

	onMount(() => {
		void views.load();
	});
</script>

<svelte:head>
	<title>{m.savedViewsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<SavedViewTable />
</div>
<RenameViewDialog />
