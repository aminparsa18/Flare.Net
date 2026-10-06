<script lang="ts">
	// Mirrors OnCallRotationTable.svelte's shape. See docs-internal/adr/0128-synthetic-monitoring.md.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Switch } from '$lib/components/ui/switch';
	import { syntheticMonitorsContext } from '$lib/synthetic-monitors/context';
	import type { SyntheticMonitor } from '$lib/synthetic-monitors-api';
	import { formatDateTime } from '$lib/time/format';
	import { goto } from '$app/navigation';
	import { authContext } from '$lib/auth/context';
	import { buildAlertDeepLinkHref } from '$lib/deep-links';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import BellPlusIcon from '@lucide/svelte/icons/bell-plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import RadarIcon from '@lucide/svelte/icons/radar';

	const monitors = syntheticMonitorsContext.get();
	const auth = authContext.get();

	function interval(seconds: number): string {
		if (seconds % 3600 === 0) return m.synthetic_intervalHours({ count: seconds / 3600 });
		if (seconds % 60 === 0) return m.synthetic_intervalMinutes({ count: seconds / 60 });
		return m.synthetic_intervalSeconds({ count: seconds });
	}

	/** Opens the alert form pre-filled for this monitor: `synthetic.up`, filtered to it, with the "N of M locations" picker. */
	function createAlert(monitor: SyntheticMonitor): void {
		void goto(
			buildAlertDeepLinkHref({
				kind: 'MetricThreshold',
				name: m.synthetic_alertName({ name: monitor.name }),
				metricName: 'synthetic.up',
				metricType: 'Gauge',
				syntheticMonitor: monitor.name
			})
		);
	}

	async function handleDelete(monitor: SyntheticMonitor): Promise<void> {
		if (!confirm(m.synthetic_deleteConfirm({ name: monitor.name }))) return;
		await monitors.remove(monitor.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.synthetic_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.synthetic_description()}</p>
	</div>
	<Button size="sm" onclick={() => monitors.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.synthetic_newMonitor()}
	</Button>
</div>

{#if monitors.loading && monitors.monitors.length === 0}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if monitors.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{monitors.error}</p>
	</div>
{:else if monitors.monitors.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<RadarIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.synthetic_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.synthetic_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => monitors.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.synthetic_newMonitor()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.synthetic_colName()}</Table.Head>
					<Table.Head>{m.synthetic_colType()}</Table.Head>
					<Table.Head>{m.synthetic_colTarget()}</Table.Head>
					<Table.Head>{m.synthetic_colInterval()}</Table.Head>
					<Table.Head>{m.synthetic_colLocations()}</Table.Head>
					<Table.Head>{m.synthetic_colStatus()}</Table.Head>
					<Table.Head>{m.synthetic_colEnabled()}</Table.Head>
					<Table.Head class="text-right">{m.synthetic_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each monitors.monitors as monitor (monitor.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{monitor.name}
							{#if monitor.description}
								<p class="text-muted-foreground font-normal">{monitor.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell><Badge variant="secondary">{monitor.kind}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-80 truncate font-mono text-xs" title={monitor.target}>
							{monitor.target}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{interval(monitor.intervalSeconds)}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">
							{monitor.locations?.length ? monitor.locations.join(', ') : m.synthetic_locationsAll()}
						</Table.Cell>
						<Table.Cell title={monitor.latest ? m.synthetic_statusChecked({ time: formatDateTime(monitor.latest.time) }) : undefined}>
							{#if !monitor.latest}
								<span class="text-muted-foreground text-xs">{m.synthetic_statusNoData()}</span>
							{:else}
								<Badge variant={monitor.latest.up ? 'secondary' : 'destructive'}>
									{monitor.latest.up ? m.synthetic_statusUp() : m.synthetic_statusDown()}
								</Badge>
								{#if monitor.latest.durationMs !== null}
									<span class="text-muted-foreground ml-1 text-xs">{Math.round(monitor.latest.durationMs)} ms</span>
								{/if}
								{#if (monitor.locationStatuses?.length ?? 0) > 1}
									<div class="mt-1 flex flex-wrap gap-1">
										{#each monitor.locationStatuses ?? [] as loc (loc.location)}
											<Badge
												variant={loc.status.up ? 'outline' : 'destructive'}
												title={m.synthetic_statusChecked({ time: formatDateTime(loc.status.time) })}
											>
												{loc.location}{loc.status.durationMs !== null ? ` ${Math.round(loc.status.durationMs)} ms` : ''}
											</Badge>
										{/each}
									</div>
								{/if}
							{/if}
						</Table.Cell>
						<Table.Cell>
							<Switch size="sm" checked={monitor.enabled} onCheckedChange={(v) => monitors.setEnabled(monitor, v)} />
						</Table.Cell>
						<Table.Cell class="text-right">
							{#if auth.canMutate}
								<Button variant="ghost" size="icon-sm" title={m.synthetic_actionCreateAlert()} onclick={() => createAlert(monitor)}>
									<BellPlusIcon />
								</Button>
							{/if}
							<Button variant="ghost" size="icon-sm" title={m.synthetic_actionEdit()} onclick={() => monitors.openEdit(monitor)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.synthetic_actionDelete()}
								onclick={() => handleDelete(monitor)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}
