<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { ErrorsExplorerState } from '$lib/errors/state.svelte';
	import { errorsExplorerContext } from '$lib/errors/context';
	import ErrorsToolbar from '$lib/components/errors/ErrorsToolbar.svelte';
	import ExceptionGroupsTable from '$lib/components/errors/ExceptionGroupsTable.svelte';
	import ExceptionOccurrenceDialog from '$lib/components/errors/ExceptionOccurrenceDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const errors = errorsExplorerContext.set(new ErrorsExplorerState());

	onMount(() => {
		void errors.runSearch();
	});

	onDestroy(() => {
		errors.dispose();
	});
</script>

<svelte:head>
	<title>{m.errorsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<ErrorsToolbar />
	<div class="min-h-0 flex-1 overflow-auto">
		<ExceptionGroupsTable />
	</div>
</div>
<ExceptionOccurrenceDialog />
