<script lang="ts">
	// Flare's analog to Seq's "Ingestion Log" link - unlike Seq (a dedicated screen), this
	// is inline: the recent-errors list is already small (capped at 200 server-side, see
	// IngestionStatsKeys.MaxErrorEntries) and this is the only page that would ever want
	// it, so a separate route would just be an extra click for no benefit.
	//
	// Filterable to one (signal, protocol) via ingestion.logFilter (Planning.md v10
	// follow-up) - set by RejectedTelemetryDialog's "View rejected payloads" action, so
	// clicking a Rejected count up in IngestionSignalsTable lands here already scoped to
	// what was clicked, instead of the reader having to eyeball-filter a mixed list.
	//
	// A normal-flow block section (not flex-1/min-h-0) - that sizing was left over from
	// when this was the last thing on the page and could fairly claim "whatever's left" of
	// the scrollable body's height. It no longer is (Pipeline health, then Ingestion
	// topology, both render after it) - flex-1 on a *non-last* child of a flex column still
	// tries to fill the remaining space against those later siblings' own height, which
	// squeezes this section below its content's actual size and lets that content visually
	// overflow on top of Pipeline health below it instead of properly reserving room for it.
	// The table now caps its own height and scrolls internally instead.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import XIcon from '@lucide/svelte/icons/x';
	import { ingestionContext } from '$lib/ingestion/context';
	import { protocolLabel } from '$lib/ingestion/format';

	const ingestion = ingestionContext.get();

	const filter = $derived(ingestion.logFilter);
	const errors = $derived(
		(ingestion.stats?.recentErrors ?? []).filter(
			(e) => !filter || (e.signal === filter.signal && e.protocol === filter.protocol)
		)
	);

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}
</script>

<div id="ingestion-log" class="border-t px-4 py-3">
	<div class="mb-2 flex items-center gap-2">
		<h2 class="text-sm font-medium">Ingestion log</h2>
		{#if filter}
			<Badge variant="outline" class="gap-1">
				{filter.signal} · {protocolLabel(filter.protocol)}
				<button type="button" onclick={() => ingestion.clearLogFilter()} aria-label="Clear filter" class="cursor-pointer">
					<XIcon class="size-3" />
				</button>
			</Badge>
		{/if}
	</div>
	{#if errors.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>{filter ? 'No matching rejected payloads' : 'No rejected payloads'}</Empty.Title>
				{#if filter}
					<Empty.Description>
						No recent rejections for {filter.signal} · {protocolLabel(filter.protocol)}.
						<Button variant="link" size="sm" class="h-auto p-0" onclick={() => ingestion.clearLogFilter()}>Clear filter</Button>
					</Empty.Description>
				{:else}
					<Empty.Description>Malformed OTLP exports (bad protobuf/JSON, unsupported content type) will show up here.</Empty.Description>
				{/if}
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="max-h-[320px] overflow-auto">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>Time</Table.Head>
						<Table.Head>Receiver</Table.Head>
						<Table.Head>Reason</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each errors as entry, i (entry.timestamp + i)}
						<Table.Row>
							<Table.Cell class="text-muted-foreground whitespace-nowrap text-xs">{formatTime(entry.timestamp)}</Table.Cell>
							<Table.Cell>
								<Badge variant="outline">{entry.signal} · {entry.protocol}</Badge>
							</Table.Cell>
							<Table.Cell class="text-destructive font-mono text-xs">{entry.reason}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{/if}
</div>