<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import ServerIcon from '@lucide/svelte/icons/server';
	import { hostsContext } from '$lib/hosts/context';
	import type { HostsSortColumn } from '$lib/hosts/state.svelte';
	import { formatPercent } from '$lib/indexing/format';
	import * as m from '$lib/paraglide/messages';
	import { formatDateTime } from '$lib/time/format';

	const hosts = hostsContext.get();

	// >=90% reads as saturated, >=75% as worth a look - same two-tier warning/destructive
	// escalation ServicesTable's errorRateClass uses, with thresholds for utilization.
	function utilizationClass(percent: number): string {
		if (percent >= 90) return 'text-destructive font-medium';
		if (percent >= 75) return 'text-warning';
		return '';
	}

	// Several missed scrapes at the receiver's default 60s interval - the host has most
	// likely stopped reporting, even though it's still inside the window.
	const STALE_AFTER_MS = 5 * 60_000;

	const now = $derived(hosts.loadedAt);

	function formatAgo(iso: string): string {
		const minutes = Math.floor((now - new Date(iso).getTime()) / 60_000);
		if (minutes < 1) return m.hostsPage_justNow();
		if (minutes < 60) return m.hostsPage_minutesAgo({ minutes });
		return m.hostsPage_hoursAgo({ hours: Math.floor(minutes / 60) });
	}

	interface ColumnDef {
		column: HostsSortColumn | null;
		label: string;
		align: 'left' | 'right';
	}

	const columns = $derived<ColumnDef[]>([
		{ column: 'hostName', label: m.hostsPage_hostColumn(), align: 'left' },
		{ column: null, label: m.hostsPage_osColumn(), align: 'left' },
		{ column: 'cpuPercent', label: m.hostsPage_cpuColumn(), align: 'right' },
		{ column: 'memoryPercent', label: m.hostsPage_memoryColumn(), align: 'right' },
		{ column: 'diskPercent', label: m.hostsPage_diskColumn(), align: 'right' },
		{ column: 'loadAverage15m', label: m.hostsPage_loadColumn(), align: 'right' },
		{ column: 'lastSeen', label: m.hostsPage_lastSeenColumn(), align: 'right' }
	]);
</script>

{#snippet percentCell(value: number | null)}
	<Table.Cell class="text-right tabular-nums {value == null ? '' : utilizationClass(value)}">
		{#if value == null}
			<span class="text-muted-foreground" title={m.hostsPage_noDataTooltip()}>&mdash;</span>
		{:else}
			{formatPercent(value)}
		{/if}
	</Table.Cell>
{/snippet}

<div class="px-4 pb-4">
	{#if hosts.error}
		<p class="text-destructive py-2 text-xs">{hosts.error}</p>
	{/if}
	{#if hosts.loading && !hosts.hosts}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !hosts.hosts || hosts.hosts.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><ServerIcon /></Empty.Media>
				<Empty.Title>{m.hostsPage_noHostsTitle()}</Empty.Title>
				<Empty.Description>{m.hostsPage_noHostsDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if hosts.truncated}
			<p class="text-warning py-2 text-xs">{m.hostsPage_truncated({ count: hosts.hosts.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.label)}
						{@const active = col.column !== null && hosts.sortColumn === col.column}
						<Table.Head
							class={col.align === 'right' ? 'text-right' : ''}
							aria-sort={col.column === null ? undefined : active ? (hosts.sortDescending ? 'descending' : 'ascending') : 'none'}
						>
							{#if col.column === null}
								{col.label}
							{:else}
								{@const column = col.column}
								<button
									type="button"
									class="hover:text-foreground inline-flex items-center gap-1 {col.align === 'right' ? 'flex-row-reverse' : ''} {active
										? 'text-foreground'
										: ''}"
									onclick={() => hosts.setSort(column)}
								>
									{col.label}
									{#if active}
										{#if hosts.sortDescending}
											<ChevronDownIcon class="size-3" />
										{:else}
											<ChevronUpIcon class="size-3" />
										{/if}
									{:else}
										<ArrowUpDownIcon class="text-muted-foreground/50 size-3" />
									{/if}
								</button>
							{/if}
						</Table.Head>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each hosts.sorted() as host (host.hostName)}
					{@const stale = now - new Date(host.lastSeen).getTime() > STALE_AFTER_MS}
					<Table.Row class={stale ? 'text-muted-foreground' : ''}>
						<Table.Cell class="font-medium">
							<button type="button" class="hover:underline" onclick={() => hosts.openHost(host.hostName)}>
								{host.hostName}
							</button>
						</Table.Cell>
						<Table.Cell>{host.osType ?? '—'}</Table.Cell>
						{@render percentCell(host.cpuPercent)}
						{@render percentCell(host.memoryPercent)}
						{@render percentCell(host.diskPercent)}
						<Table.Cell class="text-right tabular-nums">
							{#if host.loadAverage15m == null}
								<span class="text-muted-foreground" title={m.hostsPage_noDataTooltip()}>&mdash;</span>
							{:else}
								{host.loadAverage15m.toFixed(2)}
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(host.lastSeen)}>
							{formatAgo(host.lastSeen)}
							{#if stale}
								<span class="text-warning ml-1">· {m.hostsPage_stale()}</span>
							{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
