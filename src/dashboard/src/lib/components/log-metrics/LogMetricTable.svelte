<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Switch } from '$lib/components/ui/switch';
	import { Spinner } from '$lib/components/ui/spinner';
	import { authContext } from '$lib/auth/context';
	import { logMetricsContext } from '$lib/log-metrics/context';
	import { summarizeCondition } from '$lib/log-metrics/format';
	import type { LogMetric } from '$lib/log-metrics-api';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import SigmaIcon from '@lucide/svelte/icons/sigma';

	const auth = authContext.get();
	const logMetrics = logMetricsContext.get();

	async function handleDelete(metric: LogMetric): Promise<void> {
		if (!confirm(m.logMetricTable_deleteConfirm({ name: metric.name }))) return;
		await logMetrics.remove(metric.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.logMetricTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.logMetricTable_subheading()}</p>
	</div>
	{#if auth.canMutate}
		<Button size="sm" onclick={() => logMetrics.openCreate()}>
			<PlusIcon data-icon="inline-start" />
			{m.logMetricTable_new()}
		</Button>
	{/if}
</div>

{#if logMetrics.loading && logMetrics.metrics.length === 0}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if logMetrics.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{logMetrics.error}</p>
	</div>
{:else if logMetrics.metrics.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<SigmaIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.logMetricTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.logMetricTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		{#if auth.canMutate}
			<Empty.Content>
				<Button size="sm" onclick={() => logMetrics.openCreate()}>
					<PlusIcon data-icon="inline-start" />
					{m.logMetricTable_new()}
				</Button>
			</Empty.Content>
		{/if}
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.logMetricTable_colName()}</Table.Head>
					<Table.Head>{m.logMetricTable_colMetric()}</Table.Head>
					<Table.Head>{m.logMetricTable_colCondition()}</Table.Head>
					<Table.Head>{m.logMetricTable_colGroupBy()}</Table.Head>
					<Table.Head>{m.logMetricTable_colEnabled()}</Table.Head>
					{#if auth.canMutate}
						<Table.Head class="text-right">{m.logMetricTable_colActions()}</Table.Head>
					{/if}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each logMetrics.metrics as metric (metric.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{metric.name}
							{#if metric.description}
								<p class="text-muted-foreground font-normal">{metric.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="font-mono text-xs">{metric.metricName}</Table.Cell>
						<Table.Cell class="text-muted-foreground">{summarizeCondition(metric.condition, m.logMetricTable_allLogs())}</Table.Cell>
						<Table.Cell class="text-muted-foreground font-mono text-xs">{metric.groupBy.join(', ') || '—'}</Table.Cell>
						<Table.Cell>
							<Switch
								checked={metric.enabled}
								disabled={!auth.canMutate}
								onCheckedChange={(v) => logMetrics.setEnabled(metric, v)}
								aria-label={m.logMetricTable_colEnabled()}
							/>
						</Table.Cell>
						{#if auth.canMutate}
							<Table.Cell class="text-right">
								<Button variant="ghost" size="icon-sm" title={m.logMetricTable_edit()} onclick={() => logMetrics.openEdit(metric)}>
									<PencilIcon />
								</Button>
								<Button
									variant="ghost"
									size="icon-sm"
									class="text-destructive hover:text-destructive"
									title={m.logMetricTable_delete()}
									onclick={() => handleDelete(metric)}
								>
									<Trash2Icon />
								</Button>
							</Table.Cell>
						{/if}
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}
