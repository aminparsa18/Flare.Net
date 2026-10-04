<script lang="ts">
	// Admin-only ingest API key management: create/revoke plus per-key ingestion limits
	// and live usage (ADR-0051). Reachable from NavUserMenu's dropdown, like /access-tokens.
	// Usage re-polls quietly every 10s - the per-minute column is only meaningful if it
	// moves while you watch it.
	import { onMount } from 'svelte';
	import { IngestKeysState } from '$lib/ingest-keys/state.svelte';
	import { ingestKeysContext } from '$lib/ingest-keys/context';
	import IngestKeyTable from '$lib/components/ingest-keys/IngestKeyTable.svelte';
	import CreateIngestKeyDialog from '$lib/components/ingest-keys/CreateIngestKeyDialog.svelte';
	import IngestKeyLimitsDialog from '$lib/components/ingest-keys/IngestKeyLimitsDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const REFRESH_INTERVAL_MS = 10_000;

	const keys = ingestKeysContext.set(new IngestKeysState());

	onMount(() => {
		void keys.load();
		const timer = setInterval(() => {
			if (!document.hidden && !keys.createOpen && !keys.limitsTarget) void keys.load(true);
		}, REFRESH_INTERVAL_MS);
		return () => clearInterval(timer);
	});
</script>

<svelte:head>
	<title>{m.ingestKeysPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<IngestKeyTable />
</div>
<CreateIngestKeyDialog />
<IngestKeyLimitsDialog />
