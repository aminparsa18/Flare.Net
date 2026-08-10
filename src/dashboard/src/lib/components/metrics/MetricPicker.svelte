<script lang="ts">
	import * as Command from '$lib/components/ui/command';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import type { MetricNameInfo, MetricPointType } from '$lib/metrics-api';
	import { cn } from '$lib/utils';

	const explorer = metricsExplorerContext.get();

	function isSelected(metric: MetricNameInfo): boolean {
		return (
			explorer.selected?.metricName === metric.metricName && explorer.selected?.serviceName === metric.serviceName
		);
	}

	// Fixed per-type colors, not the chart's own categorical series palette - this
	// badge identifies a metric's *kind*, a closed 3-value set completely unrelated to
	// which color a given chart line gets (MetricChart assigns series colors
	// independently, per selected metric, from --chart-1.. in a fixed slot order - see
	// its own remarks).
	const TYPE_BADGE_VARIANT: Record<MetricPointType, 'secondary' | 'outline' | 'default'> = {
		Gauge: 'secondary',
		Sum: 'outline',
		Histogram: 'default'
	};
</script>

<div class="flex w-72 shrink-0 flex-col border-r">
	<Command.Root class="flex min-h-0 flex-1 flex-col rounded-none bg-transparent">
		<Command.Input placeholder="Filter metrics..." />
		<Command.List class="min-h-0 flex-1">
			{#if explorer.namesLoading && explorer.names.length === 0}
				<div class="flex justify-center py-8">
					<Spinner />
				</div>
			{:else if explorer.namesError}
				<p class="text-destructive px-3 py-4 text-xs">{explorer.namesError}</p>
			{:else}
				<Command.Empty>No metrics found for the current filters.</Command.Empty>
				<Command.Group>
					{#each explorer.names as metric (metric.metricName + '' + metric.serviceName)}
						<Command.Item
							value="{metric.metricName} {metric.serviceName}"
							onSelect={() => explorer.selectMetric(metric)}
							class={cn('items-start', isSelected(metric) && 'bg-accent text-accent-foreground')}
						>
							<div class="flex min-w-0 flex-1 flex-col py-0.5">
								<span class="truncate text-sm">{metric.metricName}</span>
								<span class="text-muted-foreground truncate text-xs">
									{metric.serviceName}{metric.unit ? ` · ${metric.unit}` : ''}
								</span>
							</div>
							<Badge variant={TYPE_BADGE_VARIANT[metric.type]} class="mt-0.5 shrink-0">{metric.type}</Badge>
						</Command.Item>
					{/each}
				</Command.Group>
			{/if}
		</Command.List>
	</Command.Root>
</div>
