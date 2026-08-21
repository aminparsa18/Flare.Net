<script lang="ts">
	// Resources page's first section - "is the machine this runs on okay?" answered before
	// the reader ever gets to the Docker graph below. Independent of that graph's own
	// enablement (see routes/resources/+page.svelte) - HostStatsState/HostStatsSnapshot are
	// their own stream, so this renders (or shows its own unavailable state) regardless of
	// whether DockerResources:ProxyUrl is configured.
	//
	// CPU/Memory get a meter bar per the dataviz skill's "Meter" contract: the fill carries
	// severity (accent -> warning -> danger) and the unfilled track is a lighter step of
	// that same hue, so state reads across the whole bar, not just the fill. Same 90%/75%
	// severity thresholds $lib/components/indexing/IndexingSummaryTiles.svelte's disk-usage
	// card already established for this dashboard, kept in sync rather than invented fresh.
	// Disk/Uptime stay plain numbers, same as the original design brief - not every stat
	// needs a bar, and a fixed 100% wouldn't take an operator's total-disk decision, unlike
	// CPU/Memory where "how much headroom is left" is the entire point.
	import * as Card from '$lib/components/ui/card';
	import { Badge } from '$lib/components/ui/badge';
	import { Empty, EmptyHeader, EmptyMedia, EmptyTitle, EmptyDescription } from '$lib/components/ui/empty';
	import { formatByteUsage, formatUptime } from '$lib/resources/format';
	import type { HostStatsSnapshot } from '$lib/api';
	import CpuIcon from '@lucide/svelte/icons/cpu';
	import MemoryStickIcon from '@lucide/svelte/icons/memory-stick';
	import HardDriveIcon from '@lucide/svelte/icons/hard-drive';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import ActivityIcon from '@lucide/svelte/icons/activity';

	let { snapshot }: { snapshot: HostStatsSnapshot | null } = $props();

	function severityTextClass(percent: number): string {
		if (percent >= 90) return 'text-destructive';
		if (percent >= 75) return 'text-warning';
		return '';
	}

	function meterClasses(percent: number): { fill: string; track: string } {
		if (percent >= 90) return { fill: 'bg-destructive', track: 'bg-destructive/15' };
		if (percent >= 75) return { fill: 'bg-warning', track: 'bg-warning/15' };
		return { fill: 'bg-primary', track: 'bg-primary/15' };
	}

	const cpuPercent = $derived(snapshot ? Math.round(snapshot.cpuUsagePercent) : 0);
	const memPercent = $derived(
		snapshot && snapshot.memoryTotalBytes > 0
			? Math.round((snapshot.memoryUsedBytes / snapshot.memoryTotalBytes) * 100)
			: 0
	);
	const diskPercent = $derived(
		snapshot && snapshot.diskTotalBytes > 0
			? Math.round((snapshot.diskUsedBytes / snapshot.diskTotalBytes) * 100)
			: 0
	);
</script>

{#snippet meter(percent: number)}
	{@const cls = meterClasses(percent)}
	<div class="h-1.5 w-full overflow-hidden rounded-full {cls.track}">
		<div class="h-full rounded-full {cls.fill}" style="width: {Math.min(100, Math.max(0, percent))}%"></div>
	</div>
{/snippet}

{#if !snapshot || !snapshot.available}
	<div class="border-b px-4 py-3">
		<Empty class="py-6">
			<EmptyHeader>
				<EmptyMedia variant="icon">
					<ActivityIcon />
				</EmptyMedia>
				<EmptyTitle>Host overview unavailable</EmptyTitle>
				<EmptyDescription>
					{snapshot?.unavailableReason ?? 'Waiting for Flare.Api...'}
				</EmptyDescription>
			</EmptyHeader>
		</Empty>
	</div>
{:else}
	<div class="border-b px-4 py-3">
		<div class="mb-3 flex items-center gap-2">
			<span class="text-sm font-medium">Docker host running Flare.Net</span>
			<Badge variant="outline" class="gap-1.5">
				<span class="size-1.5 rounded-full" style="background: var(--chart-3)"></span>
				Healthy
			</Badge>
		</div>
		<div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
			<Card.Root>
				<Card.Header>
					<Card.Description class="flex items-center gap-1.5">
						<CpuIcon class="size-3.5" />
						CPU
					</Card.Description>
					<Card.Title class="text-2xl tabular-nums {severityTextClass(cpuPercent)}">{cpuPercent}%</Card.Title>
				</Card.Header>
				<Card.Content class="flex flex-col gap-1.5">
					{@render meter(cpuPercent)}
					<span class="text-muted-foreground text-xs">
						{snapshot.cpuCoreCount} core{snapshot.cpuCoreCount === 1 ? '' : 's'} · load {snapshot.loadAverage1m.toFixed(2)}
					</span>
				</Card.Content>
			</Card.Root>

			<Card.Root>
				<Card.Header>
					<Card.Description class="flex items-center gap-1.5">
						<MemoryStickIcon class="size-3.5" />
						Memory
					</Card.Description>
					<Card.Title class="text-2xl tabular-nums {severityTextClass(memPercent)}">
						{formatByteUsage(snapshot.memoryUsedBytes, snapshot.memoryTotalBytes)}
					</Card.Title>
				</Card.Header>
				<Card.Content class="flex flex-col gap-1.5">
					{@render meter(memPercent)}
					<span class="text-muted-foreground text-xs">{memPercent}% used</span>
				</Card.Content>
			</Card.Root>

			<Card.Root>
				<Card.Header>
					<Card.Description class="flex items-center gap-1.5">
						<HardDriveIcon class="size-3.5" />
						Disk
					</Card.Description>
					<Card.Title class="text-2xl tabular-nums {severityTextClass(diskPercent)}">
						{formatByteUsage(snapshot.diskUsedBytes, snapshot.diskTotalBytes)}
					</Card.Title>
				</Card.Header>
				<Card.Content class="text-muted-foreground text-xs">{diskPercent}% used</Card.Content>
			</Card.Root>

			<Card.Root>
				<Card.Header>
					<Card.Description class="flex items-center gap-1.5">
						<ClockIcon class="size-3.5" />
						Uptime
					</Card.Description>
					<Card.Title class="text-2xl tabular-nums">{formatUptime(snapshot.uptimeSeconds)}</Card.Title>
				</Card.Header>
				<Card.Content class="text-muted-foreground text-xs">since last boot</Card.Content>
			</Card.Root>
		</div>
	</div>
{/if}
