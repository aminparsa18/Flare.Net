<script lang="ts">
	// No drill-down sheet of its own - a namespace is a filter, so a row opens the Pods tab
	// narrowed to it (the Workloads/Volumes tabs share the same namespace filter).
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import FolderIcon from '@lucide/svelte/icons/folder';
	import SortableHead from './SortableHead.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import type { NamespacesSortColumn } from '$lib/kubernetes/state.svelte';
	import { formatAgo, isStale } from '$lib/kubernetes/ago';
	import { formatBytes, formatCores } from '$lib/kubernetes/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();
	const now = $derived(k8s.loadedAt);

	const columns = $derived<{ column: NamespacesSortColumn; label: string; align: 'left' | 'right' }[]>([
		{ column: 'namespace', label: m.kubernetesPage_namespaceColumn(), align: 'left' },
		{ column: 'phase', label: m.kubernetesPage_statusColumn(), align: 'left' },
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
	{#if k8s.loading && !k8s.namespaces}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if !k8s.namespaces || k8s.namespaces.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><FolderIcon /></Empty.Media>
				<Empty.Title>{m.kubernetesPage_noNamespacesTitle()}</Empty.Title>
				<Empty.Description>{m.kubernetesPage_noNamespacesDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if k8s.namespacesTruncated}
			<p class="text-warning py-2 text-xs">{m.kubernetesPage_truncated({ count: k8s.namespaces.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.column)}
						<SortableHead
							label={col.label}
							align={col.align}
							active={k8s.namespacesSortColumn === col.column}
							descending={k8s.namespacesSortDescending}
							onSort={() => k8s.setNamespacesSort(col.column)}
						/>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each k8s.sortedNamespaces() as ns (ns.namespace)}
					{@const stale = isStale(ns.lastSeen, now)}
					<Table.Row class={stale ? 'text-muted-foreground' : ''}>
						<Table.Cell class="max-w-72 truncate font-medium">
							<button type="button" class="hover:underline" title={ns.namespace} onclick={() => k8s.showPodsInNamespace(ns.namespace)}>
								{ns.namespace}
							</button>
						</Table.Cell>
						<Table.Cell>
							{#if ns.phase}
								<Badge variant={ns.phase === 'Terminating' ? 'warning' : 'outline'}>{ns.phase}</Badge>
							{:else}{@render noData()}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if ns.podCount == null}{@render noData()}{:else}{ns.podCount}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if ns.cpuCores == null}{@render noData()}{:else}{formatCores(ns.cpuCores)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if ns.memoryWorkingSetBytes == null}{@render noData()}{:else}{formatBytes(ns.memoryWorkingSetBytes)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(ns.lastSeen)}>
							{formatAgo(ns.lastSeen, now)}
							{#if stale}<span class="text-warning ml-1">· {m.hostsPage_stale()}</span>{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
