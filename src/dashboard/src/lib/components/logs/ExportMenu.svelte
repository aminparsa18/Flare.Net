<script lang="ts">
	// Exports the Logs Explorer's currently-filtered result set (not just what's loaded on
	// screen - see export.ts's own remarks) as a downloaded file. Icon-only Popover trigger
	// (same footprint as PatternsModal/ShareViewButton) opening two plain actions rather than
	// a Command list, since the choice is a fixed pair (CSV/XLSX), not a searchable list -
	// see SavedSearchesMenu.svelte for the Command-list variant of this same Popover shape.
	// No format-picker dialog: picking a format starts the export immediately, the chosen
	// item's icon becomes a spinner while paginating, and an alert() reports truncation/
	// errors - same native-dialog convention SavedSearchesMenu's handleDelete already uses
	// for confirm(), since there's no toast system in this repo.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import FileTextIcon from '@lucide/svelte/icons/file-text';
	import FileSpreadsheetIcon from '@lucide/svelte/icons/file-spreadsheet';
	import { logsExplorerContext } from '$lib/logs/context';
	import {
		fetchAllForExport,
		eventsToCsv,
		eventsToXlsxBlob,
		exportFilename,
		downloadBlob,
		type ExportFormat
	} from '$lib/logs/export';

	const explorer = logsExplorerContext.get();

	let open = $state(false);
	let exportingFormat = $state<ExportFormat | null>(null);
	let abortController: AbortController | null = null;

	async function handleExport(format: ExportFormat): Promise<void> {
		if (exportingFormat) return;
		open = false;
		exportingFormat = format;
		abortController = new AbortController();
		const range = explorer.currentRange();
		try {
			const { events, truncated } = await fetchAllForExport(explorer.buildFilter(range), abortController.signal);
			const blob = format === 'csv' ? new Blob([eventsToCsv(events)], { type: 'text/csv;charset=utf-8' }) : eventsToXlsxBlob(events);
			downloadBlob(blob, exportFilename(range, truncated, format));
			if (truncated) {
				alert(
					`Export limited to the first ${events.length.toLocaleString()} matching rows. Narrow the time range or filters to export the rest.`
				);
			}
		} catch (err) {
			if (abortController.signal.aborted) return;
			alert(`Export failed: ${err instanceof Error ? err.message : String(err)}`);
		} finally {
			exportingFormat = null;
			abortController = null;
		}
	}

	// Abort an in-flight export if the toolbar (and therefore this button) is torn down
	// mid-fetch, same "don't let a stale request finish into a gone component" reasoning
	// as LogsExplorerState.#searchAbort/dispose.
	$effect(() => {
		return () => abortController?.abort();
	});
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="icon-sm" title="Export" disabled={exportingFormat !== null}>
				{#if exportingFormat}
					<Spinner class="size-3" />
				{:else}
					<DownloadIcon />
				{/if}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-48 p-1" align="start">
		<Button variant="ghost" size="sm" class="w-full justify-start" onclick={() => handleExport('csv')}>
			<FileTextIcon data-icon="inline-start" />
			Export as CSV
		</Button>
		<Button variant="ghost" size="sm" class="w-full justify-start" onclick={() => handleExport('xlsx')}>
			<FileSpreadsheetIcon data-icon="inline-start" />
			Export as XLSX
		</Button>
	</Popover.Content>
</Popover.Root>
