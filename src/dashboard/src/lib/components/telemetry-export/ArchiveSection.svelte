<script lang="ts">
	// S3 archive (ADR-0156/0157): the saved settings form plus the worker's per-table export status.
	import { onDestroy, onMount } from 'svelte';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { getArchiveSettings, getArchiveStatus, type ArchiveSettings, type ArchiveStatus } from '$lib/telemetry-export-api';
	import ArchiveSettingsForm from './ArchiveSettingsForm.svelte';
	import ArchiveStatusTable from './ArchiveStatusTable.svelte';
	import * as m from '$lib/paraglide/messages';

	const POLL_MS = 10000;

	let settings = $state.raw<ArchiveSettings | null>(null);
	/** Bumped after every save/reset so the form remounts with the server's (re-masked) values. */
	let formVersion = $state(0);
	let status = $state.raw<ArchiveStatus | null>(null);
	let error = $state<string | null>(null);
	/** Outcome of the last save/reset; kept here because the form remounts when it succeeds. */
	let notice = $state<string | null>(null);
	let timer: ReturnType<typeof setTimeout> | undefined;
	let controller: AbortController | undefined;

	async function loadSettings() {
		try {
			settings = await getArchiveSettings();
			formVersion++;
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function pollStatus() {
		controller = new AbortController();
		try {
			status = await getArchiveStatus(controller.signal);
		} catch (err) {
			if (!(err instanceof DOMException && err.name === 'AbortError')) error = err instanceof Error ? err.message : String(err);
		}
		timer = setTimeout(pollStatus, POLL_MS);
	}

	async function changed(message: string) {
		notice = message;
		await loadSettings();
		clearTimeout(timer);
		void pollStatus();
	}

	onMount(() => {
		void loadSettings();
		void pollStatus();
	});

	onDestroy(() => {
		clearTimeout(timer);
		controller?.abort();
	});
</script>

<section class="flex flex-col gap-3">
	<div>
		<h2 class="text-base font-semibold">{m.telemetryExport_archiveHeading()}</h2>
		<p class="text-muted-foreground text-sm">{m.telemetryExport_archiveIntro()}</p>
	</div>

	{#if error}
		<p class="text-destructive text-sm">{error}</p>
	{/if}

	{#if settings}
		<div class="flex items-center gap-2 text-sm">
			{#if status}
				<Badge variant={status.active ? 'secondary' : 'outline'}>
					{status.active ? m.telemetryExport_archiveActive() : m.telemetryExport_archiveInactive()}
				</Badge>
				{#if status.active && status.source}
					<span class="text-muted-foreground">
						{status.source === 'settings' ? m.telemetryExport_archiveSourceSettings() : m.telemetryExport_archiveSourceConfig()}
					</span>
				{/if}
			{/if}
			{#if !settings.saved}
				<span class="text-muted-foreground">{m.telemetryExport_archiveUsingConfig()}</span>
			{/if}
		</div>

		{#key formVersion}
			<ArchiveSettingsForm {settings} onchanged={changed} />
		{/key}
		{#if notice}
			<p class="text-xs text-emerald-600 dark:text-emerald-400">{notice}</p>
		{/if}
	{:else if !error}
		<div class="flex h-24 items-center justify-center"><Spinner /></div>
	{/if}

	{#if status && status.tables.length > 0}
		<div>
			<h3 class="mb-2 text-sm font-medium">{m.telemetryExport_archiveStatusHeading()}</h3>
			<ArchiveStatusTable {status} />
		</div>
	{/if}
</section>
