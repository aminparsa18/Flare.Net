<script lang="ts">
	// Mirrors MaintenanceWindowTable.svelte's shape. See
	// docs-internal/adr/0126-alert-oncall-rotations.md.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { onCallRotationsContext } from '$lib/oncall-rotations/context';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import type { OnCallRotationStatus } from '$lib/oncall-rotations-api';
	import { formatDateTimeMinutes } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import PhoneCallIcon from '@lucide/svelte/icons/phone-call';

	const oncall = onCallRotationsContext.get();
	const channels = notificationChannelsContext.get();

	function channelName(id: string): string {
		return channels.channels.find((c) => c.id === id)?.name ?? m.oncall_unknownChannel();
	}

	function shiftLength(hours: number): string {
		return hours % 24 === 0 ? m.oncall_shiftDays({ count: hours / 24 }) : m.oncall_shiftHoursValue({ count: hours });
	}

	async function handleDelete(status: OnCallRotationStatus): Promise<void> {
		if (!confirm(m.oncall_deleteConfirm({ name: status.rotation.name }))) return;
		await oncall.remove(status.rotation.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.oncall_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.oncall_description()}</p>
	</div>
	<Button size="sm" onclick={() => oncall.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.oncall_newRotation()}
	</Button>
</div>

{#if oncall.loading && oncall.rotations.length === 0}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if oncall.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{oncall.error}</p>
	</div>
{:else if oncall.rotations.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<PhoneCallIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.oncall_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.oncall_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => oncall.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.oncall_newRotation()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.oncall_colName()}</Table.Head>
					<Table.Head>{m.oncall_colParticipants()}</Table.Head>
					<Table.Head>{m.oncall_colShift()}</Table.Head>
					<Table.Head>{m.oncall_colOnCall()}</Table.Head>
					<Table.Head class="text-right">{m.oncall_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each oncall.rotations as status (status.rotation.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{status.rotation.name}
							{#if status.rotation.description}
								<p class="text-muted-foreground font-normal">{status.rotation.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">
							{status.rotation.channelIds.map(channelName).join(' → ')}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{shiftLength(status.rotation.shiftHours)}</Table.Cell>
						<Table.Cell>
							<Badge variant="secondary">{channelName(status.onCallChannelId)}</Badge>
							{#if status.isOverride}
								<Badge variant="outline">{m.oncall_override()}</Badge>
							{/if}
							<p class="text-muted-foreground mt-1 text-xs">{m.oncall_until({ time: formatDateTimeMinutes(status.shiftEndsAt) })}</p>
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.oncall_actionEdit()} onclick={() => oncall.openEdit(status.rotation)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.oncall_actionDelete()}
								onclick={() => handleDelete(status)}
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
