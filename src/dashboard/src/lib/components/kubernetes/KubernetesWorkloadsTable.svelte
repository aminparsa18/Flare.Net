<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import SortableHead from './SortableHead.svelte';
	import KubernetesWorkloadStatus from './KubernetesWorkloadStatus.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import type { WorkloadsSortColumn } from '$lib/kubernetes/state.svelte';
	import { formatAgo, isStale } from '$lib/kubernetes/ago';
	import { formatBytes, formatCores } from '$lib/kubernetes/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();
	const now = $derived(k8s.loadedAt);
	const kind = $derived(k8s.workloadsKind);

	const columns = $derived<{ column: WorkloadsSortColumn; label: string; align: 'left' | 'right' }[]>([
		{ column: 'name', label: m.kubernetesPage_nameColumn(), align: 'left' },
		{ column: 'namespace', label: m.kubernetesPage_namespaceColumn(), align: 'left' },
		{ column: 'status', label: m.kubernetesPage_statusColumn(), align: 'left' },
		{ column: 'podCount', label: m.kubernetesPage_podsColumn(), align: 'right' },
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
	{#if k8s.loading && !k8s.workloads}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if !k8s.workloads || k8s.workloads.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><LayersIcon /></Empty.Media>
				<Empty.Title>{m.kubernetesPage_noWorkloadsTitle()}</Empty.Title>
				<Empty.Description>{m.kubernetesPage_noWorkloadsDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if k8s.workloadsTruncated}
			<p class="text-warning py-2 text-xs">{m.kubernetesPage_truncated({ count: k8s.workloads.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.column)}
						<SortableHead
							label={col.label}
							align={col.align}
							active={k8s.workloadsSortColumn === col.column}
							descending={k8s.workloadsSortDescending}
							onSort={() => k8s.setWorkloadsSort(col.column)}
						/>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each k8s.sortedWorkloads() as workload (workload.namespace + '/' + workload.name)}
					{@const stale = isStale(workload.lastSeen, now)}
					<Table.Row class={stale ? 'text-muted-foreground' : ''}>
						<Table.Cell class="max-w-72 truncate font-medium">
							<button
								type="button"
								class="hover:underline"
								title={workload.name}
								onclick={() => k8s.openWorkload({ kind, namespace: workload.namespace, name: workload.name })}
							>
								{workload.name}
							</button>
						</Table.Cell>
						<Table.Cell>{workload.namespace}</Table.Cell>
						<Table.Cell><KubernetesWorkloadStatus {kind} counts={workload} /></Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if workload.podCount == null}{@render noData()}{:else}{workload.podCount}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if workload.cpuCores == null}{@render noData()}{:else}{formatCores(workload.cpuCores)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if workload.memoryWorkingSetBytes == null}{@render noData()}{:else}{formatBytes(workload.memoryWorkingSetBytes)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(workload.lastSeen)}>
							{formatAgo(workload.lastSeen, now)}
							{#if stale}<span class="text-warning ml-1">· {m.hostsPage_stale()}</span>{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
