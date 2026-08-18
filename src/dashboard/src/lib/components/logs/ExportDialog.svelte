<script lang="ts">
	// Exports the Logs Explorer's logs as a downloaded file. Asks the user which rows and
	// which format, rather than assuming - a first cut of this feature always exported the
	// full filtered result set silently, which read as "wrong" to anyone who expected an
	// export of what's actually on screen (e.g. after scrolling/loading more, or a bounded
	// live-tail scrollback). Two independent choices, both funneled into export.ts's shared
	// pipeline (eventsToBlob dispatches on `format`):
	//   - Rows: "visible" (exactly LogsExplorerState.events, no fetch, instant) vs.
	//     "filtered" (the full filtered result set, paginated via /api/logs/search, bounded
	//     by EXPORT_ROW_CAP).
	//   - Format: CSV, XLSX, JSON, or XML.
	// A small Dialog rather than the icon-only Popover this started as (see git history) -
	// two real decisions to make before downloading warrants a confirm step, unlike Share's
	// single fire-and-forget action.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import { logsExplorerContext } from '$lib/logs/context';
	import type { LogEventDto } from '$lib/api';
	import { fetchAllForExport, eventsToBlob, exportFilename, downloadBlob, type ExportFormat, type ExportScope } from '$lib/logs/export';

	const FORMAT_OPTIONS: { value: ExportFormat; label: string }[] = [
		{ value: 'csv', label: 'CSV' },
		{ value: 'xlsx', label: 'XLSX' },
		{ value: 'json', label: 'JSON' },
		{ value: 'xml', label: 'XML' }
	];

	const explorer = logsExplorerContext.get();

	// Bindable (rather than fully owned here) so LogsToolbar can open this same dialog
	// from outside - specifically, from the "Export Logs" command palette action, which
	// reaches it via active-explorer.svelte.ts's openExport since the palette isn't a
	// descendant of this component. The Dialog.Trigger button below still opens it the
	// old way too - both paths just flip the same flag.
	let { open = $bindable(false) }: { open?: boolean } = $props();
	let scope = $state<ExportScope>('visible');
	let format = $state<ExportFormat>('csv');
	let exporting = $state(false);
	let error = $state<string | null>(null);
	let abortController: AbortController | null = null;

	$effect(() => {
		if (open) {
			error = null;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (exporting) return; // let the in-flight export finish/abort via Cancel instead of a stray outside click
		open = next;
	}

	function handleCancel(): void {
		if (exporting) {
			abortController?.abort();
			return;
		}
		open = false;
	}

	async function handleExport(): Promise<void> {
		if (exporting) return;
		exporting = true;
		error = null;
		const range = explorer.currentRange();
		try {
			let events: LogEventDto[];
			let truncated = false;
			if (scope === 'visible') {
				events = explorer.events;
			} else {
				abortController = new AbortController();
				({ events, truncated } = await fetchAllForExport(explorer.buildFilter(range), abortController.signal));
			}
			downloadBlob(eventsToBlob(events, format), exportFilename(range, truncated, format, scope));
			open = false;
			if (truncated) {
				alert(
					`Export limited to the first ${events.length.toLocaleString()} matching rows. Narrow the time range or filters to export the rest.`
				);
			}
		} catch (err) {
			if (abortController?.signal.aborted) {
				open = false; // user hit Cancel mid-fetch - not an error
			} else {
				error = err instanceof Error ? err.message : String(err);
			}
		} finally {
			exporting = false;
			abortController = null;
		}
	}

	// Abort an in-flight export if the toolbar (and therefore this dialog) is torn down
	// mid-fetch, same "don't let a stale request finish into a gone component" reasoning
	// as LogsExplorerState.#searchAbort/dispose.
	$effect(() => {
		return () => abortController?.abort();
	});
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="icon-sm" title="Export">
				<DownloadIcon />
			</Button>
		{/snippet}
	</Dialog.Trigger>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>Export logs</Dialog.Title>
			<Dialog.Description>Choose which rows and which format to download.</Dialog.Description>
		</Dialog.Header>

		<div class="space-y-4">
			<div class="space-y-2">
				<span class="text-sm font-medium">Rows</span>
				<div class="flex gap-2">
					<Button
						type="button"
						variant={scope === 'visible' ? 'default' : 'outline'}
						size="sm"
						class="flex-1"
						onclick={() => (scope = 'visible')}
					>
						Currently shown ({explorer.events.length.toLocaleString()})
					</Button>
					<Button
						type="button"
						variant={scope === 'filtered' ? 'default' : 'outline'}
						size="sm"
						class="flex-1"
						onclick={() => (scope = 'filtered')}
					>
						All matching the filter
					</Button>
				</div>
				<p class="text-muted-foreground text-xs">
					{#if scope === 'visible'}
						Exactly the rows currently loaded in the table below.
					{:else}
						Fetches every row matching the current filter and time range (up to 25,000 rows) - may take a
						moment for a wide range.
					{/if}
				</p>
			</div>

			<div class="space-y-2">
				<span class="text-sm font-medium">Format</span>
				<div class="grid grid-cols-2 gap-2">
					{#each FORMAT_OPTIONS as option (option.value)}
						<Button
							type="button"
							variant={format === option.value ? 'default' : 'outline'}
							size="sm"
							onclick={() => (format = option.value)}
						>
							{option.label}
						</Button>
					{/each}
				</div>
			</div>

			{#if error}
				<p class="text-destructive text-sm">{error}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button type="button" variant="outline" onclick={handleCancel}>Cancel</Button>
			<Button type="button" disabled={exporting} onclick={handleExport}>
				{#if exporting}<Spinner class="size-4" />{/if}
				Export
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
