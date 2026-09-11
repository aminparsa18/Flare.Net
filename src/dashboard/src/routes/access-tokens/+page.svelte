<script lang="ts">
	// Self-service personal access tokens (ADR-0019) - reachable from NavUserMenu's
	// dropdown, not the persistent top nav (same "click-to-reveal" placement as
	// /data-sources, which that page's own comment explains). Mirrors routes/views's
	// shape: a state class set into context, one table component.
	import { onMount } from 'svelte';
	import { AccessTokensState } from '$lib/access-tokens/state.svelte';
	import { accessTokensContext } from '$lib/access-tokens/context';
	import AccessTokenTable from '$lib/components/access-tokens/AccessTokenTable.svelte';
	import CreateAccessTokenDialog from '$lib/components/access-tokens/CreateAccessTokenDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const tokens = accessTokensContext.set(new AccessTokensState());

	onMount(() => {
		void tokens.load();
	});
</script>

<svelte:head>
	<title>{m.accessTokensPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<AccessTokenTable />
</div>
<CreateAccessTokenDialog />
