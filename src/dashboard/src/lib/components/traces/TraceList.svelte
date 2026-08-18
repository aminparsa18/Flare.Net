<script lang="ts">
	import VirtualList from '$lib/components/virtual-list/VirtualList.svelte';
	import TraceRow from './TraceRow.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { tracesExplorerContext } from '$lib/traces/context';

	const explorer = tracesExplorerContext.get();

	const ROW_HEIGHT = 32;
	// Same shared-CSS-custom-property trick as LogTable, so the header and every
	// TraceRow can never drift out of alignment.
	const COLUMNS = '170px 90px 160px 1fr 90px 70px'; // Time | Status | Service | Name | Duration | Spans
</script>

<div
	class="flex min-h-0 flex-1 flex-col"
	style:--trace-row-columns={COLUMNS}
	style:--trace-row-height="{ROW_HEIGHT}px"
>
	<div
		class="bg-muted/30 text-muted-foreground grid shrink-0 items-center gap-3 overflow-y-hidden border-b px-3 text-xs font-medium"
		style="grid-template-columns: var(--trace-row-columns); height: 28px; scrollbar-gutter: stable;"
	>
		<span>Time</span>
		<span>Status</span>
		<span>Service</span>
		<span>Name</span>
		<span>Duration</span>
		<span class="text-right">Spans</span>
	</div>

	{#if explorer.traces.length === 0 && !explorer.loading}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>No traces</Empty.Title>
				<Empty.Description>No traces match the current filters.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<VirtualList
			items={explorer.traces}
			itemHeight={ROW_HEIGHT}
			getKey={(trace) => trace.traceId}
			ariaLabel="Traces"
			onEndReached={() => void explorer.loadMore()}
			class="min-h-0 flex-1"
		>
			{#snippet children(trace)}
				<TraceRow {trace} />
			{/snippet}
		</VirtualList>
		{#if explorer.loadingMore}
			<div class="flex shrink-0 items-center justify-center border-t py-2">
				<Spinner />
			</div>
		{/if}
	{/if}
</div>
