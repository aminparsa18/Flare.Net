<script lang="ts">
	// Per-table progress of the S3 archive (ADR-0156/0157): last finished ingest hour exported, its row
	// count and the last error. Shared by the Telemetry export settings page and the Ingestion page card.
	import * as Table from '$lib/components/ui/table';
	import type { ArchiveStatus, ArchiveTableStatus } from '$lib/telemetry-export-api';
	import { singleLineError } from '$lib/telemetry-export/format';
	import { formatAge, formatCount, secondsSince } from '$lib/ingestion/format';
	import { formatDateTimeMinutes } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	let { status }: { status: ArchiveStatus } = $props();

	/** An error only reads as current when no export has succeeded since. */
	function errorIsCurrent(row: ArchiveTableStatus): boolean {
		if (!row.lastError) return false;
		if (!row.lastSuccessAt || !row.lastErrorAt) return true;
		return new Date(row.lastErrorAt) >= new Date(row.lastSuccessAt);
	}
</script>

<Table.Root>
	<Table.Header>
		<Table.Row>
			<Table.Head>{m.telemetryExport_archiveColTable()}</Table.Head>
			<Table.Head>{m.telemetryExport_archiveColHour()}</Table.Head>
			<Table.Head class="text-right">{m.telemetryExport_archiveColRows()}</Table.Head>
			<Table.Head class="text-right">{m.telemetryExport_archiveColLastSuccess()}</Table.Head>
			<Table.Head>{m.telemetryExport_archiveColError()}</Table.Head>
		</Table.Row>
	</Table.Header>
	<Table.Body>
		{#each status.tables as row (row.table)}
			<Table.Row>
				<Table.Cell class="font-mono text-xs font-medium">{row.table}</Table.Cell>
				<Table.Cell class="tabular-nums">{row.lastExportedHour ? formatDateTimeMinutes(row.lastExportedHour) : '—'}</Table.Cell>
				<Table.Cell class="text-right tabular-nums">{row.lastExportedHour ? formatCount(row.lastRows) : '—'}</Table.Cell>
				<Table.Cell class="text-muted-foreground text-right tabular-nums">
					{row.lastSuccessAt ? formatAge(secondsSince(row.lastSuccessAt)) : m.telemetryExport_never()}
				</Table.Cell>
				<Table.Cell class="max-w-xs truncate font-mono text-xs" title={singleLineError(row.lastError) ?? undefined}>
					{#if row.lastError}
						<span class={errorIsCurrent(row) ? 'text-destructive' : 'text-muted-foreground'}>{singleLineError(row.lastError)}</span>
					{:else}
						<span class="text-muted-foreground">—</span>
					{/if}
				</Table.Cell>
			</Table.Row>
		{:else}
			<Table.Row>
				<Table.Cell colspan={5} class="text-muted-foreground text-center text-sm">{m.telemetryExport_archiveNoTables()}</Table.Cell>
			</Table.Row>
		{/each}
	</Table.Body>
</Table.Root>
