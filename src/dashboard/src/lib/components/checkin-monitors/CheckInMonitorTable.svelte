<script lang="ts">
	// Mirrors SyntheticMonitorTable.svelte's shape. See docs-internal/adr/0260-cron-job-checkins.md.
	import * as Table from '#lib/components/ui/table/index.ts';
	import * as Empty from '#lib/components/ui/empty/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { Switch } from '#lib/components/ui/switch/index.ts';
	import { checkInMonitorsContext } from '#lib/checkin-monitors/context.ts';
	import { pingCommand, type CheckInMonitor, type CheckInState } from '#lib/checkin-monitors-api.ts';
	import { formatDateTime } from '#lib/time/format.ts';
	import { goto } from '$app/navigation';
	import { authContext } from '#lib/auth/context.ts';
	import { buildAlertDeepLinkHref } from '#lib/deep-links.ts';
	import * as m from '#lib/paraglide/messages.js';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import BellPlusIcon from '@lucide/svelte/icons/bell-plus';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import HeartPulseIcon from '@lucide/svelte/icons/heart-pulse';

	const monitors = checkInMonitorsContext.get();
	const auth = authContext.get();

	const STATE_LABELS: Record<CheckInState, () => string> = {
		Pending: () => m.checkin_statePending(),
		Ok: () => m.checkin_stateOk(),
		Running: () => m.checkin_stateRunning(),
		Late: () => m.checkin_stateLate(),
		Failed: () => m.checkin_stateFailed(),
		Overran: () => m.checkin_stateOverran(),
		Disabled: () => m.checkin_stateDisabled()
	};

	function badgeVariant(state: CheckInState): 'secondary' | 'destructive' | 'outline' {
		if (state === 'Late' || state === 'Failed' || state === 'Overran') return 'destructive';
		return state === 'Disabled' ? 'outline' : 'secondary';
	}

	function every(seconds: number): string {
		if (seconds % 3600 === 0) return m.synthetic_intervalHours({ count: seconds / 3600 });
		if (seconds % 60 === 0) return m.synthetic_intervalMinutes({ count: seconds / 60 });
		return m.synthetic_intervalSeconds({ count: seconds });
	}

	function schedule(monitor: CheckInMonitor): string {
		return monitor.cronExpression ? `${monitor.cronExpression} (${monitor.timeZone})` : every(monitor.intervalSeconds);
	}

	/** Opens the alert form pre-filled for this monitor: `checkin.up`, filtered to its slug. */
	function createAlert(monitor: CheckInMonitor): void {
		void goto(
			buildAlertDeepLinkHref({
				kind: 'MetricThreshold',
				name: m.checkin_alertName({ name: monitor.name }),
				metricName: 'checkin.up',
				metricType: 'Gauge',
				syntheticMonitor: monitor.slug
			})
		);
	}

	async function copyPing(monitor: CheckInMonitor): Promise<void> {
		try {
			await navigator.clipboard.writeText(pingCommand(monitor.slug));
		} catch {
			// Clipboard blocked (insecure context): nothing to fall back to.
		}
	}

	async function handleDelete(monitor: CheckInMonitor): Promise<void> {
		if (!confirm(m.checkin_deleteConfirm({ name: monitor.name }))) return;
		await monitors.remove(monitor.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.checkin_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.checkin_description()}</p>
	</div>
	<Button size="sm" onclick={() => monitors.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.checkin_newMonitor()}
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
				<HeartPulseIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.checkin_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.checkin_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => monitors.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.checkin_newMonitor()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.checkin_colName()}</Table.Head>
					<Table.Head>{m.checkin_colSchedule()}</Table.Head>
					<Table.Head>{m.checkin_colStatus()}</Table.Head>
					<Table.Head>{m.checkin_colLastPing()}</Table.Head>
					<Table.Head>{m.checkin_colNextExpected()}</Table.Head>
					<Table.Head>{m.checkin_colEnabled()}</Table.Head>
					<Table.Head class="text-right">{m.checkin_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each monitors.monitors as monitor (monitor.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{monitor.name}
							<p class="text-muted-foreground font-mono text-xs font-normal">{monitor.slug}</p>
							{#if monitor.description}
								<p class="text-muted-foreground font-normal">{monitor.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground font-mono text-xs">{schedule(monitor)}</Table.Cell>
						<Table.Cell>
							{#if monitor.status}
								<Badge variant={badgeVariant(monitor.status.state)}>{STATE_LABELS[monitor.status.state]()}</Badge>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">
							{monitor.status?.lastPing ? formatDateTime(monitor.status.lastPing) : m.checkin_never()}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">
							{monitor.status?.nextExpected && monitor.enabled ? formatDateTime(monitor.status.nextExpected) : ''}
						</Table.Cell>
						<Table.Cell>
							<Switch size="sm" checked={monitor.enabled} onCheckedChange={(v) => monitors.setEnabled(monitor, v)} />
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.checkin_actionCopyPing()} onclick={() => copyPing(monitor)}>
								<CopyIcon />
							</Button>
							{#if auth.canMutate}
								<Button variant="ghost" size="icon-sm" title={m.checkin_actionCreateAlert()} onclick={() => createAlert(monitor)}>
									<BellPlusIcon />
								</Button>
							{/if}
							<Button variant="ghost" size="icon-sm" title={m.checkin_actionEdit()} onclick={() => monitors.openEdit(monitor)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.checkin_actionDelete()}
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
