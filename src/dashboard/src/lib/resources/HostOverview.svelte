<script lang="ts">
	// Resources page's first section - "is the machine this runs on okay?" answered before
	// the reader ever gets to the topology graph below. Independent of that graph's own
	// enablement (see routes/resources/+page.svelte) - HostStatsState/HostStatsSnapshot are
	// their own stream (reading Linux /proc directly, not Docker/Kubernetes-specific at
	// all), so this renders (or shows its own unavailable state) regardless of whether
	// DockerResources:ProxyUrl/KubernetesResources:Enabled is configured.
	//
	// CPU/Memory get a meter bar per the dataviz skill's "Meter" contract: the fill carries
	// severity (accent -> warning -> danger) and the unfilled track is a lighter step of
	// that same hue, so state reads across the whole bar, not just the fill. Same 90%/75%
	// severity thresholds $lib/components/indexing/IndexingSummaryTiles.svelte's disk-usage
	// card already established for this dashboard, kept in sync rather than invented fresh.
	// Disk/Uptime stay plain numbers - not every stat needs a bar, and a fixed 100% wouldn't
	// take an operator's total-disk decision, unlike CPU/Memory where "how much headroom is
	// left" is the entire point.
	//
	// Network stays out of the tile row entirely - a compact "down/up" line in the header
	// instead (see the live RX/TX line below), matching the brief this shipped from: "you
	// don't need to show this as a giant card." Per-core CPU is an expandable accordion
	// under the CPU tile rather than a separate detail view/route - same reasoning, this
	// panel already *is* the detail view for anything host-related.
	import * as Card from '$lib/components/ui/card';
	import * as Accordion from '$lib/components/ui/accordion';
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { Badge } from '$lib/components/ui/badge';
	import { Empty, EmptyHeader, EmptyMedia, EmptyTitle, EmptyDescription } from '$lib/components/ui/empty';
	import { formatBytes } from '$lib/ingestion/format';
	import {
		formatByteUsage,
		formatUptime,
		formatByteRate,
		formatByteGrowthPerDay,
		formatPacketRate
	} from '$lib/resources/format';
	import { WARNING_THRESHOLD_PERCENT, CRITICAL_THRESHOLD_PERCENT } from '$lib/resources/thresholds';
	import { computeHostHealth } from '$lib/resources/host-health';
	import { resourcesContext } from '$lib/resources/context';
	import HostTrendChart from '$lib/resources/HostTrendChart.svelte';
	import HostHealth from '$lib/resources/HostHealth.svelte';
	import type { HostStatsHistoryPoint, HostStatsSnapshot } from '$lib/api';
	import CpuIcon from '@lucide/svelte/icons/cpu';
	import MemoryStickIcon from '@lucide/svelte/icons/memory-stick';
	import HardDriveIcon from '@lucide/svelte/icons/hard-drive';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import ActivityIcon from '@lucide/svelte/icons/activity';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';

	let { snapshot, history }: { snapshot: HostStatsSnapshot | null; history: HostStatsHistoryPoint[] } = $props();

	// Set by +page.svelte before this component renders - see context.ts's own convention
	// ("every descendant calls .get() instead of receiving it as a prop"). Only used here
	// for the Docker/Kubernetes row in the Host health section below (whichever provider is
	// actually configured - see host-health.ts's remarks) - independent of everything else
	// on this panel, which stays HostStatsState-only.
	const resources = resourcesContext.get();

	function severityTextClass(percent: number): string {
		if (percent >= CRITICAL_THRESHOLD_PERCENT) return 'text-destructive';
		if (percent >= WARNING_THRESHOLD_PERCENT) return 'text-warning';
		return '';
	}

	function meterClasses(percent: number): { fill: string; track: string } {
		if (percent >= CRITICAL_THRESHOLD_PERCENT) return { fill: 'bg-destructive', track: 'bg-destructive/15' };
		if (percent >= WARNING_THRESHOLD_PERCENT) return { fill: 'bg-warning', track: 'bg-warning/15' };
		return { fill: 'bg-primary', track: 'bg-primary/15' };
	}

	// Only meaningful once snapshot is available (the {:else} branch below) - host-health.ts
	// handles a null snapshot's fields fine either way, but there's nothing to check before
	// then. Also drives the header badge (isHealthy below), so "Healthy" stops being a
	// hardcoded literal and actually reflects these checks.
	const healthChecks = $derived(snapshot ? computeHostHealth(snapshot, resources.snapshot) : []);
	const isHealthy = $derived(!healthChecks.some((check) => check.tone === 'warning'));

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

	// "" is bits-ui's own closed-accordion value for type="single" - same convention
	// VolumeChart.svelte's own collapse/expand accordion uses.
	let perCoreAccordionValue = $state('');
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
		<div class="mb-3 flex flex-wrap items-center gap-x-4 gap-y-1">
			<div class="flex items-center gap-2">
				<span class="text-sm font-medium">Host running Flare.Net</span>
				<Badge variant="outline" class="gap-1.5">
					<span class="size-1.5 rounded-full" style="background: {isHealthy ? 'var(--chart-3)' : 'var(--warning)'}"></span>
					{isHealthy ? 'Healthy' : 'Needs attention'}
				</Badge>
			</div>
			<!-- Compact live RX/TX - deliberately not a tile, see the file header remarks. -->
			<div class="text-muted-foreground ml-auto flex items-center gap-3 text-xs tabular-nums">
				<span class="flex items-center gap-1" title="Received">
					<ArrowDownIcon class="size-3" />
					{formatByteRate(snapshot.networkRxBytesPerSecond)}
				</span>
				<span class="flex items-center gap-1" title="Sent">
					<ArrowUpIcon class="size-3" />
					{formatByteRate(snapshot.networkTxBytesPerSecond)}
				</span>
				<span>{formatPacketRate(snapshot.networkPacketsPerSecond)}</span>
			</div>
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
					{#if snapshot.perCoreUsagePercent.length > 0}
						<Accordion.Root type="single" bind:value={perCoreAccordionValue} class="w-full border-0">
							<Accordion.Item value="cores" class="border-0">
								<Accordion.Trigger
									class="text-muted-foreground hover:text-foreground relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
								>
									Per-core breakdown ({snapshot.perCoreUsagePercent.length} cores)
								</Accordion.Trigger>
								<Accordion.Content class="p-0 pt-2">
									<div class="grid grid-cols-2 gap-x-3 gap-y-1.5">
										{#each snapshot.perCoreUsagePercent as corePercent, i (i)}
											{@const rounded = Math.round(corePercent)}
											<div class="flex items-center gap-1.5">
												<span class="text-muted-foreground w-9 shrink-0 text-xs tabular-nums">core {i}</span>
												{@render meter(rounded)}
												<span class="w-8 shrink-0 text-right text-xs tabular-nums {severityTextClass(rounded)}">{rounded}%</span>
											</div>
										{/each}
									</div>
								</Accordion.Content>
							</Accordion.Item>
						</Accordion.Root>
					{/if}
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
					<span class="text-muted-foreground text-xs">
						{memPercent}% used · {formatBytes(snapshot.memoryAvailableBytes)} available
					</span>
					<!-- Swap disabled entirely (0 total) is common under Docker Desktop and
					     various cloud VM images - hidden rather than showing "0 B swap"
					     everywhere. -->
					{#if snapshot.swapTotalBytes > 0}
						<span class="text-muted-foreground text-xs">+ {formatByteUsage(snapshot.swapUsedBytes, snapshot.swapTotalBytes)} swap</span>
					{/if}
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
				<Card.Content class="flex flex-col gap-1.5">
					<span class="text-muted-foreground text-xs">{diskPercent}% used</span>
					<!-- null until HostStatsOptions.DiskGrowthMinimumSpan worth of samples has
					     accumulated - omitted entirely rather than shown as a placeholder, see
					     HostStatsSnapshot.cs's own remarks. -->
					{#if snapshot.diskGrowthBytesPerDay !== null}
						{@const diskGrowthBytesPerDay = snapshot.diskGrowthBytesPerDay}
						<Tooltip.Provider>
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}
										<span
											{...props}
											class="text-muted-foreground w-fit text-xs underline decoration-dotted underline-offset-2"
										>
											{formatByteGrowthPerDay(diskGrowthBytesPerDay)}
										</span>
									{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content>
									Based on the trailing {snapshot.diskGrowthWindowHours < 1
										? `${Math.round(snapshot.diskGrowthWindowHours * 60)}m`
										: `${snapshot.diskGrowthWindowHours.toFixed(1)}h`}
								</Tooltip.Content>
							</Tooltip.Root>
						</Tooltip.Provider>
					{/if}
					<span class="text-muted-foreground text-xs">
						read {formatByteRate(snapshot.diskReadBytesPerSecond)} · write {formatByteRate(snapshot.diskWriteBytesPerSecond)}
					</span>
				</Card.Content>
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

		<HostTrendChart {history} />
		<HostHealth checks={healthChecks} />
	</div>
{/if}
