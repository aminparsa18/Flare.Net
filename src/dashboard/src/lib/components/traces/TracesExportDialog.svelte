<script lang="ts">
	// Exports the Traces explorer's list as CSV or NDJSON with a chosen column subset. Same
	// two-decision shape as logs/ExportDialog.svelte (which rows, which format) plus the
	// columns; the pipeline lives in `$lib/traces/export.ts`.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Spinner } from '$lib/components/ui/spinner';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import { tracesExplorerContext } from '$lib/traces/context';
	import {
		DEFAULT_TRACE_EXPORT_COLUMNS,
		TRACE_EXPORT_COLUMNS,
		downloadBlob,
		fetchAllTracesForExport,
		traceExportFilename,
		tracesToBlob,
		type TraceExportFormat,
		type TraceExportScope
	} from '$lib/traces/export';
	import type { SpanDto } from '$lib/traces-api';
	import * as m from '$lib/paraglide/messages';

	const explorer = tracesExplorerContext.get();

	let open = $state(false);
	let scope = $state<TraceExportScope>('visible');
	let format = $state<TraceExportFormat>('csv');
	let columns = $state<string[]>([...DEFAULT_TRACE_EXPORT_COLUMNS]);
	let exporting = $state(false);
	let error = $state<string | null>(null);
	let abortController: AbortController | null = null;

	$effect(() => {
		if (open) error = null;
	});

	function toggleColumn(id: string, on: boolean): void {
		columns = on ? [...columns, id] : columns.filter((c) => c !== id);
	}

	function handleOpenChange(next: boolean): void {
		if (exporting) return;
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
		if (exporting || columns.length === 0) return;
		exporting = true;
		error = null;
		const range = explorer.currentRange();
		try {
			let spans: SpanDto[];
			let truncated = false;
			if (scope === 'visible') {
				spans = explorer.traces;
			} else {
				abortController = new AbortController();
				({ spans, truncated } = await fetchAllTracesForExport(
					explorer.buildFilter(range),
					{ sortBy: explorer.filter.sortBy, sortAscending: explorer.filter.sortAscending },
					abortController.signal
				));
			}
			downloadBlob(tracesToBlob(spans, columns, format), traceExportFilename(range, truncated, format, scope));
			open = false;
			if (truncated) alert(m.tracesExport_truncatedAlert({ count: spans.length.toLocaleString() }));
		} catch (err) {
			if (abortController?.signal.aborted) {
				open = false;
			} else {
				error = err instanceof Error ? err.message : String(err);
			}
		} finally {
			exporting = false;
			abortController = null;
		}
	}

	$effect(() => {
		return () => abortController?.abort();
	});
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="icon-sm" title={m.tracesExport_export()}>
				<DownloadIcon />
			</Button>
		{/snippet}
	</Dialog.Trigger>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.tracesExport_title()}</Dialog.Title>
			<Dialog.Description>{m.tracesExport_description()}</Dialog.Description>
		</Dialog.Header>

		<div class="space-y-4">
			<div class="space-y-2">
				<span class="text-sm font-medium">{m.exportDialog_rowsLabel()}</span>
				<div class="flex gap-2">
					<Button type="button" variant={scope === 'visible' ? 'default' : 'outline'} size="sm" class="flex-1" onclick={() => (scope = 'visible')}>
						{m.exportDialog_rowsVisible({ count: explorer.traces.length.toLocaleString() })}
					</Button>
					<Button type="button" variant={scope === 'filtered' ? 'default' : 'outline'} size="sm" class="flex-1" onclick={() => (scope = 'filtered')}>
						{m.exportDialog_rowsFiltered()}
					</Button>
				</div>
				<p class="text-muted-foreground text-xs">
					{scope === 'visible' ? m.tracesExport_scopeVisibleHint() : m.exportDialog_scopeFilteredHint()}
				</p>
			</div>

			<div class="space-y-2">
				<span class="text-sm font-medium">{m.exportDialog_formatLabel()}</span>
				<div class="grid grid-cols-2 gap-2">
					<Button type="button" variant={format === 'csv' ? 'default' : 'outline'} size="sm" onclick={() => (format = 'csv')}>CSV</Button>
					<Button type="button" variant={format === 'ndjson' ? 'default' : 'outline'} size="sm" onclick={() => (format = 'ndjson')}>NDJSON</Button>
				</div>
			</div>

			<div class="space-y-2">
				<span class="text-sm font-medium">{m.tracesExport_columnsLabel()}</span>
				<div class="grid grid-cols-2 gap-x-4 gap-y-1.5">
					{#each TRACE_EXPORT_COLUMNS as col (col.id)}
						<label class="flex items-center gap-2 text-sm">
							<Checkbox checked={columns.includes(col.id)} onCheckedChange={(v) => toggleColumn(col.id, v === true)} />
							{col.header}
						</label>
					{/each}
				</div>
			</div>

			{#if error}
				<p class="text-destructive text-sm">{error}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button type="button" variant="outline" onclick={handleCancel}>{m.exportDialog_cancel()}</Button>
			<Button type="button" disabled={exporting || columns.length === 0} onclick={handleExport}>
				{#if exporting}<Spinner class="size-4" />{/if}
				{m.tracesExport_export()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
