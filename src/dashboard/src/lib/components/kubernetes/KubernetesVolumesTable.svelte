<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import HardDriveIcon from '@lucide/svelte/icons/hard-drive';
	import SortableHead from './SortableHead.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import type { VolumesSortColumn } from '$lib/kubernetes/state.svelte';
	import { formatAgo, isStale } from '$lib/kubernetes/ago';
	import { formatBytes } from '$lib/kubernetes/format';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();
	const now = $derived(k8s.loadedAt);

	const columns = $derived<{ column: VolumesSortColumn | null; label: string; align: 'left' | 'right' }[]>([
		{ column: 'volumeName', label: m.kubernetesPage_volumeColumn(), align: 'left' },
		{ column: 'namespace', label: m.kubernetesPage_namespaceColumn(), align: 'left' },
		{ column: 'podName', label: m.kubernetesPage_podColumn(), align: 'left' },
		{ column: null, label: m.kubernetesPage_typeColumn(), align: 'left' },
		{ column: 'usedBytes', label: m.kubernetesPage_usedColumn(), align: 'right' },
		{ column: 'capacityBytes', label: m.kubernetesPage_capacityColumn(), align: 'right' },
		{ column: 'usedPercent', label: m.kubernetesPage_usedPercentColumn(), align: 'right' },
		{ column: 'inodesUsedPercent', label: m.kubernetesPage_inodesColumn(), align: 'right' },
		{ column: 'lastSeen', label: m.hostsPage_lastSeenColumn(), align: 'right' }
	]);

	/** Same >=90% saturated / >=75% worth-a-look tiers as HostsTable.svelte's utilization cells. */
	function fillClass(percent: number | null): string {
		if (percent == null) return '';
		if (percent >= 90) return 'text-destructive font-medium';
		return percent >= 75 ? 'text-warning' : '';
	}
</script>

{#snippet noData()}
	<span class="text-muted-foreground" title={m.kubernetesPage_noDataTooltip()}>&mdash;</span>
{/snippet}

<div class="px-4 pb-4">
	{#if k8s.error}
		<p class="text-destructive py-2 text-xs">{k8s.error}</p>
	{/if}
	{#if k8s.loading && !k8s.volumes}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if !k8s.volumes || k8s.volumes.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><HardDriveIcon /></Empty.Media>
				<Empty.Title>{m.kubernetesPage_noVolumesTitle()}</Empty.Title>
				<Empty.Description>{m.kubernetesPage_noVolumesDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if k8s.volumesTruncated}
			<p class="text-warning py-2 text-xs">{m.kubernetesPage_truncated({ count: k8s.volumes.length })}</p>
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
							active={column !== null && k8s.volumesSortColumn === column}
							descending={k8s.volumesSortDescending}
							onSort={() => column && k8s.setVolumesSort(column)}
						/>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each k8s.sortedVolumes() as volume (volume.namespace + '/' + volume.podName + '/' + volume.volumeName)}
					{@const stale = isStale(volume.lastSeen, now)}
					<Table.Row class={stale ? 'text-muted-foreground' : ''}>
						<Table.Cell class="max-w-64 truncate font-medium">
							<button
								type="button"
								class="hover:underline"
								title={volume.claimName ?? volume.volumeName}
								onclick={() => k8s.openVolume({ namespace: volume.namespace, podName: volume.podName, volumeName: volume.volumeName })}
							>
								{volume.volumeName}
							</button>
							{#if volume.claimName && volume.claimName !== volume.volumeName}
								<span class="text-muted-foreground text-xs">{volume.claimName}</span>
							{/if}
						</Table.Cell>
						<Table.Cell>{volume.namespace}</Table.Cell>
						<Table.Cell class="max-w-56 truncate">
							<button
								type="button"
								class="hover:underline"
								title={volume.podName}
								onclick={() => k8s.openPod({ namespace: volume.namespace, podName: volume.podName })}
							>
								{volume.podName}
							</button>
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">{volume.volumeType ?? ''}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if volume.usedBytes == null}{@render noData()}{:else}{formatBytes(volume.usedBytes)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if volume.capacityBytes == null}{@render noData()}{:else}{formatBytes(volume.capacityBytes)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {fillClass(volume.usedPercent)}">
							{#if volume.usedPercent == null}{@render noData()}{:else}{formatPercent(volume.usedPercent)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {fillClass(volume.inodesUsedPercent)}">
							{#if volume.inodesUsedPercent == null}{@render noData()}{:else}{formatPercent(volume.inodesUsedPercent)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(volume.lastSeen)}>
							{formatAgo(volume.lastSeen, now)}
							{#if stale}<span class="text-warning ml-1">· {m.hostsPage_stale()}</span>{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
