<script lang="ts">
	// Mirrors NotificationChannelTable.svelte's shape. See
	// docs-internal/adr/0055-alert-maintenance-windows.md.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { maintenanceWindowsContext } from '$lib/maintenance-windows/context';
	import { instantToZoned } from '$lib/maintenance-windows/time-zone';
	import type { MaintenanceWindow } from '$lib/maintenance-windows-api';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import MoonIcon from '@lucide/svelte/icons/moon';

	const maintenance = maintenanceWindowsContext.get();

	// 2023-01-01 was a Sunday - index = System.DayOfWeek ordinal.
	const weekdayNames = Array.from({ length: 7 }, (_, i) =>
		new Date(Date.UTC(2023, 0, 1 + i)).toLocaleDateString(undefined, { weekday: 'short', timeZone: 'UTC' })
	);

	function formatInstant(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false, dateStyle: 'medium', timeStyle: 'short' });
	}

	function schedule(mw: MaintenanceWindow): string {
		if (mw.recurrence === 'None') {
			return `${formatInstant(mw.startsAt)} → ${formatInstant(mw.endsAt)}`;
		}

		const start = instantToZoned(new Date(mw.startsAt), mw.timeZone).slice(11);
		const end = instantToZoned(new Date(mw.endsAt), mw.timeZone).slice(11);
		const text =
			mw.recurrence === 'Daily'
				? m.maintenanceWindowTable_daily({ start, end })
				: m.maintenanceWindowTable_weekly({ days: mw.daysOfWeek.map((d) => weekdayNames[d]).join(', '), start, end });
		return `${text} (${mw.timeZone})`;
	}

	type Status = 'active' | 'scheduled' | 'ended';

	function status(mw: MaintenanceWindow): Status {
		if (maintenance.activeWindowIds.includes(mw.id)) return 'active';
		const now = Date.now();
		const endedAt = mw.recurrence === 'None' ? mw.endsAt : mw.repeatUntil;
		return endedAt != null && new Date(endedAt).getTime() <= now ? 'ended' : 'scheduled';
	}

	async function handleDelete(mw: MaintenanceWindow): Promise<void> {
		if (!confirm(m.maintenanceWindowTable_deleteConfirm({ name: mw.name }))) return;
		await maintenance.remove(mw.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.maintenanceWindowTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.maintenanceWindowTable_description()}</p>
	</div>
	<Button size="sm" onclick={() => maintenance.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.maintenanceWindowTable_newWindow()}
	</Button>
</div>

{#if maintenance.loading && maintenance.windows.length === 0}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if maintenance.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{maintenance.error}</p>
	</div>
{:else if maintenance.windows.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<MoonIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.maintenanceWindowTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.maintenanceWindowTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => maintenance.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.maintenanceWindowTable_newWindow()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.maintenanceWindowTable_colName()}</Table.Head>
					<Table.Head>{m.maintenanceWindowTable_colSchedule()}</Table.Head>
					<Table.Head>{m.maintenanceWindowTable_colRules()}</Table.Head>
					<Table.Head>{m.maintenanceWindowTable_colStatus()}</Table.Head>
					<Table.Head class="text-right">{m.maintenanceWindowTable_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each maintenance.windows as mw (mw.id)}
					{@const windowStatus = status(mw)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{mw.name}
							{#if mw.description}
								<p class="text-muted-foreground font-normal">{mw.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">{schedule(mw)}</Table.Cell>
						<Table.Cell class="text-muted-foreground">
							{mw.ruleIds.length === 0 ? m.maintenanceWindowTable_allRules() : m.maintenanceWindowTable_ruleCount({ count: mw.ruleIds.length })}
						</Table.Cell>
						<Table.Cell>
							<Badge variant={windowStatus === 'active' ? 'warning' : windowStatus === 'scheduled' ? 'secondary' : 'outline'}>
								{windowStatus === 'active'
									? m.maintenanceWindowTable_statusActive()
									: windowStatus === 'scheduled'
										? m.maintenanceWindowTable_statusScheduled()
										: m.maintenanceWindowTable_statusEnded()}
							</Badge>
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.maintenanceWindowTable_actionEdit()} onclick={() => maintenance.openEdit(mw)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.maintenanceWindowTable_actionDelete()}
								onclick={() => handleDelete(mw)}
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
