<script lang="ts">
	// Every external domain our services call, from client spans' `server.address`/`url.full`
	// attributes - see docs-internal/adr/0071-external-api-monitoring.md.
	import { onMount, onDestroy } from 'svelte';
	import { ExternalApisState } from '$lib/external-apis/state.svelte';
	import { externalApisContext } from '$lib/external-apis/context';
	import ExternalApisToolbar from '$lib/components/external-apis/ExternalApisToolbar.svelte';
	import ExternalApisTable from '$lib/components/external-apis/ExternalApisTable.svelte';
	import ExternalDomainSheet from '$lib/components/external-apis/ExternalDomainSheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const externalApis = externalApisContext.set(new ExternalApisState());

	onMount(() => {
		void externalApis.load();
	});

	onDestroy(() => {
		externalApis.dispose();
	});
</script>

<svelte:head>
	<title>{m.externalApisPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<ExternalApisToolbar />
	<ExternalApisTable />
	<ExternalDomainSheet />
</div>
