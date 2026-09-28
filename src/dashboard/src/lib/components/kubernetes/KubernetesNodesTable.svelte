<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import ServerIcon from '@lucide/svelte/icons/server';
	import SortableHead from './SortableHead.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import type { NodesSortColumn } from '$lib/kubernetes/state.svelte';
	import { formatAgo, isStale } from '$lib/kubernetes/ago';
	import { formatBytes, formatCores } from '$lib/kubernetes/format';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();
	const now = $derived(k8s.loadedAt);

	// Same two-tier thresholds as HostsTable's utilizationClass.
	function utilizationClass(percent: number | null): string {
		if (percent == null) return '';
		if (percent >= 90) return 'text-destructive font-medium';
		if (percent >= 75) return 'text-warning';
		return '';
	}

	const columns = $derived<{ column: NodesSortColumn; label: string; align: 'left' | 'right' }[]>([
		{ column: 'nodeName', label: m.kubernetesPage_nodeColumn(), align: 'left' },
		{ column: 'ready', label: m.kubernetesPage_statusColumn(), align: 'left' },
		{ column: 'cpuCores', label: m.kubernetesPage_cpuColumn(), align: 'right' },
		{ column: 'cpuPercent', label: m.kubernetesPage_cpuPercentColumn(), align: 'right' },
		{ column: 'memoryWorkingSetBytes', label: m.kubernetesPage_memoryColumn(), align: 'right' },
		{ column: 'memoryPercent', label: m.kubernetesPage_memoryPercentColumn(), align: 'right' },
		{ column: 'podCount', label: m.kubernetesPage_podsColumn(), align: 'right' },
		{ column: 'lastSeen', label: m.hostsPage_lastSeenColumn(), align: 'right' }
	]);
</script>

{#snippet noData()}
	<span class="text-muted-foreground" title={m.kubernetesPage_noDataTooltip()}>&mdash;</span>
{/snippet}

<div class="px-4 pb-4">
	{#if k8s.error}
		<p class="text-destructive py-2 text-xs">{k8s.error}</p>
	{/if}
	{#if k8s.loading && !k8s.nodes}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if !k8s.nodes || k8s.nodes.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><ServerIcon /></Empty.Media>
				<Empty.Title>{m.kubernetesPage_noNodesTitle()}</Empty.Title>
				<Empty.Description>{m.kubernetesPage_noNodesDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if k8s.nodesTruncated}
			<p class="text-warning py-2 text-xs">{m.kubernetesPage_truncated({ count: k8s.nodes.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.column)}
						<SortableHead
							label={col.label}
							align={col.align}
							active={k8s.nodesSortColumn === col.column}
							descending={k8s.nodesSortDescending}
							onSort={() => k8s.setNodesSort(col.column)}
						/>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each k8s.sortedNodes() as node (node.nodeName)}
					{@const stale = isStale(node.lastSeen, now)}
					<Table.Row class={stale ? 'text-muted-foreground' : ''}>
						<Table.Cell class="font-medium">
							<button type="button" class="hover:underline" onclick={() => k8s.openNode(node.nodeName)}>{node.nodeName}</button>
							{#if node.clusterName}
								<span class="text-muted-foreground ml-1 text-xs">{node.clusterName}</span>
							{/if}
						</Table.Cell>
						<Table.Cell>
							{#if node.ready === true}
								<Badge variant="outline">{m.kubernetesPage_ready()}</Badge>
							{:else if node.ready === false}
								<Badge variant="destructive">{m.kubernetesPage_notReady()}</Badge>
							{:else}
								{@render noData()}
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if node.cpuCores == null}{@render noData()}{:else}{formatCores(node.cpuCores)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {utilizationClass(node.cpuPercent)}">
							{#if node.cpuPercent == null}{@render noData()}{:else}{formatPercent(node.cpuPercent)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if node.memoryWorkingSetBytes == null}{@render noData()}{:else}{formatBytes(node.memoryWorkingSetBytes)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {utilizationClass(node.memoryPercent)}">
							{#if node.memoryPercent == null}{@render noData()}{:else}{formatPercent(node.memoryPercent)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if node.podCount == null}{@render noData()}{:else}
								<button type="button" class="hover:underline" onclick={() => k8s.showPodsOnNode(node.nodeName)}>{node.podCount}</button>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(node.lastSeen)}>
							{formatAgo(node.lastSeen, now)}
							{#if stale}<span class="text-warning ml-1">· {m.hostsPage_stale()}</span>{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
