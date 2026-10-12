<script lang="ts">
	// Ingestion-page card for the S3 archive (ADR-0157): visible to any signed-in user, hidden unless the
	// archive is active. Polls slowly - the worker only moves once per finished ingest hour.
	import { onDestroy, onMount } from 'svelte';
	import { getArchiveStatus, type ArchiveStatus } from '$lib/telemetry-export-api';
	import ArchiveStatusTable from './ArchiveStatusTable.svelte';
	import * as m from '$lib/paraglide/messages';

	const POLL_MS = 30000;

	let status = $state.raw<ArchiveStatus | null>(null);
	let timer: ReturnType<typeof setTimeout> | undefined;
	let controller: AbortController | undefined;

	async function poll() {
		controller = new AbortController();
		try {
			status = await getArchiveStatus(controller.signal);
		} catch {
			// Optional card: keep showing the last status (or nothing) rather than an error on the page.
		}
		timer = setTimeout(poll, POLL_MS);
	}

	onMount(() => void poll());
	onDestroy(() => {
		clearTimeout(timer);
		controller?.abort();
	});
</script>

{#if status?.active}
	<div class="px-4 pb-4">
		<h2 class="mb-2 text-sm font-medium">{m.telemetryExport_archiveCardHeading()}</h2>
		<ArchiveStatusTable {status} />
	</div>
{/if}
