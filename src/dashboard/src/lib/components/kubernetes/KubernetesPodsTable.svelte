<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import BoxIcon from '@lucide/svelte/icons/box';
	import SortableHead from './SortableHead.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import type { PodsSortColumn } from '$lib/kubernetes/state.svelte';
	import { formatAgo, isStale } from '$lib/kubernetes/ago';
	import { formatBytes, formatCores, phaseVariant } from '$lib/kubernetes/format';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();
	const now = $derived(k8s.loadedAt);

	const columns = $derived<{ column: PodsSortColumn | null; label: string; align: 'left' | 'right' }[]>([
		{ column: 'podName', label: m.kubernetesPage_podColumn(), align: 'left' },
		{ column: 'namespace', label: m.kubernetesPage_namespaceColumn(), align: 'left' },
		{ column: null, label: m.kubernetesPage_workloadColumn(), align: 'left' },
		{ column: 'nodeName', label: m.kubernetesPage_nodeColumn(), align: 'left' },
		{ column: 'phase', label: m.kubernetesPage_statusColumn(), align: 'left' },
		{ column: 'restarts', label: m.kubernetesPage_restartsColumn(), align: 'right' },
		{ column: 'cpuCores', label: m.kubernetesPage_cpuColumn(), align: 'right' },
		{ column: 'memoryWorkingSetBytes', label: m.kubernetesPage_memoryColumn(), align: 'right' },
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
	{#if k8s.loading && !k8s.pods}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if !k8s.pods || k8s.pods.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><BoxIcon /></Empty.Media>
				<Empty.Title>{m.kubernetesPage_noPodsTitle()}</Empty.Title>
				<Empty.Description>{m.kubernetesPage_noPodsDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if k8s.podsTruncated}
			<p class="text-warning py-2 text-xs">{m.kubernetesPage_truncated({ count: k8s.pods.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.label)}
						{@const column = col.column}
						<SortableHead
							label={col.label}
							align={col.align}
							sortable={column !== null}
							active={column !== null && k8s.podsSortColumn === column}
							descending={k8s.podsSortDescending}
							onSort={() => column && k8s.setPodsSort(column)}
						/>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each k8s.sortedPods() as pod (pod.namespace + '/' + pod.podName)}
					{@const stale = isStale(pod.lastSeen, now)}
					<Table.Row class={stale ? 'text-muted-foreground' : ''}>
						<Table.Cell class="max-w-72 truncate font-medium">
							<button type="button" class="hover:underline" title={pod.podName} onclick={() => k8s.openPod({ namespace: pod.namespace, podName: pod.podName })}>
								{pod.podName}
							</button>
						</Table.Cell>
						<Table.Cell>{pod.namespace}</Table.Cell>
						<Table.Cell class="max-w-56 truncate" title={pod.workloadName ?? undefined}>
							{#if pod.workloadName}
								<span class="text-muted-foreground text-xs">{pod.workloadKind}</span>
								{pod.workloadName}
							{:else}{@render noData()}{/if}
						</Table.Cell>
						<Table.Cell>
							{#if pod.nodeName}
								<button type="button" class="hover:underline" onclick={() => k8s.openNode(pod.nodeName!)}>{pod.nodeName}</button>
							{:else}{@render noData()}{/if}
						</Table.Cell>
						<Table.Cell>
							{#if pod.phase}<Badge variant={phaseVariant(pod.phase)}>{pod.phase}</Badge>{:else}{@render noData()}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {pod.restarts ? 'text-warning' : ''}">
							{#if pod.restarts == null}{@render noData()}{:else}{pod.restarts}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if pod.cpuCores == null}{@render noData()}{:else}
								{formatCores(pod.cpuCores)}
								{#if pod.cpuLimitPercent != null}<span class="text-muted-foreground text-xs"> · {formatPercent(pod.cpuLimitPercent)}</span>{/if}
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if pod.memoryWorkingSetBytes == null}{@render noData()}{:else}
								{formatBytes(pod.memoryWorkingSetBytes)}
								{#if pod.memoryLimitPercent != null}<span class="text-muted-foreground text-xs"> · {formatPercent(pod.memoryLimitPercent)}</span>{/if}
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(pod.lastSeen)}>
							{formatAgo(pod.lastSeen, now)}
							{#if stale}<span class="text-warning ml-1">· {m.hostsPage_stale()}</span>{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
