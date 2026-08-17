<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { patternsContext } from '$lib/patterns/context';
	import { cn } from '$lib/utils';

	const patterns = patternsContext.get();

	const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}
</script>

<div class="min-h-0 flex-1 overflow-auto px-4 pb-4">
	{#if patterns.loading && patterns.patterns.length === 0}
		<div class="flex justify-center py-12">
			<Spinner class="size-6" />
		</div>
	{:else if patterns.error}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>Couldn't load patterns</Empty.Title>
				<Empty.Description>{patterns.error}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if patterns.patterns.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>No patterns yet</Empty.Title>
				<Empty.Description>
					Patterns are computed as logs are ingested - rows written before this feature was enabled won't appear
					here. Widen the time range or check back once more traffic has flowed through.
				</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>Template</Table.Head>
					<Table.Head class="text-right">Count</Table.Head>
					<Table.Head class="text-right">Errors</Table.Head>
					<Table.Head>First seen</Table.Head>
					<Table.Head>Last seen</Table.Head>
					<Table.Head></Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each patterns.patterns as row (row.patternId)}
					<Table.Row>
						<Table.Cell class="max-w-md truncate font-mono text-xs" title={row.template}>{row.template}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{compactNumber.format(row.count)}</Table.Cell>
						<Table.Cell class={cn('text-right tabular-nums', row.errorCount > 0 && 'text-destructive')}>
							{compactNumber.format(row.errorCount)}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(row.firstSeen)}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(row.lastSeen)}</Table.Cell>
						<Table.Cell class="text-right">
							<Button
								variant="ghost"
								size="sm"
								href="/?patternId={encodeURIComponent(row.patternId)}&patternTemplate={encodeURIComponent(row.template)}"
							>
								View examples
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
