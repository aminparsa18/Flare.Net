<script lang="ts">
	// Create/edit an on-call rotation. Same Dialog + reset-on-open $effect shape as
	// MaintenanceWindowFormDialog.svelte. The first-shift start is edited as wall-clock time in the
	// browser's zone and converted to an instant on save (see $lib/time/time-zone.ts).
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { onCallRotationsContext } from '$lib/oncall-rotations/context';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import { browserTimeZone, instantToZoned, zonedToInstant } from '$lib/time/time-zone';
	import type { OnCallRotationRequest } from '$lib/oncall-rotations-api';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import XIcon from '@lucide/svelte/icons/x';

	const oncall = onCallRotationsContext.get();
	const channels = notificationChannelsContext.get();

	const open = $derived(oncall.formTarget !== null);
	const isEdit = $derived(oncall.formTarget !== null && oncall.formTarget !== 'new');
	const zone = browserTimeZone();
	const MAX_SHIFT_HOURS = 24 * 365;

	let name = $state('');
	let description = $state('');
	let participants = $state<string[]>([]);
	let shiftHoursText = $state('168');
	let startsLocal = $state('');
	// Overrides are edited as wall-clock strings in the browser's zone, like the first-shift start.
	let overrides = $state<{ channelId: string; startsLocal: string; endsLocal: string }[]>([]);

	$effect(() => {
		const target = oncall.formTarget;
		if (target === 'new') {
			name = '';
			description = '';
			participants = [''];
			shiftHoursText = '168';
			startsLocal = instantToZoned(new Date(Math.ceil(Date.now() / 3_600_000) * 3_600_000), zone);
			overrides = [];
		} else if (target) {
			name = target.name;
			description = target.description;
			participants = [...target.channelIds];
			shiftHoursText = String(target.shiftHours);
			startsLocal = instantToZoned(new Date(target.startsAt), zone);
			overrides = (target.overrides ?? []).map((o) => ({
				channelId: o.channelId,
				startsLocal: instantToZoned(new Date(o.startsAt), zone),
				endsLocal: instantToZoned(new Date(o.endsAt), zone)
			}));
		}
	});

	const channelOptions = $derived(channels.channels.map((c) => ({ value: c.id, label: `${c.name} (${c.type})` })));
	const shiftHours = $derived(Number(shiftHoursText));
	const shiftValid = $derived(Number.isInteger(shiftHours) && shiftHours >= 1 && shiftHours <= MAX_SHIFT_HOURS);
	const participantsValid = $derived(participants.length > 0 && participants.every((id) => id !== ''));
	const overridesValid = $derived(
		overrides.every(
			(o) => o.channelId !== '' && o.startsLocal !== '' && o.endsLocal !== '' && zonedToInstant(o.endsLocal, zone) > zonedToInstant(o.startsLocal, zone)
		)
	);
	const canSave = $derived(name.trim().length > 0 && shiftValid && participantsValid && overridesValid && startsLocal !== '');

	function updateOverride(index: number, patch: Partial<(typeof overrides)[number]>): void {
		overrides = overrides.map((o, i) => (i === index ? { ...o, ...patch } : o));
	}

	function addOverride(): void {
		const start = instantToZoned(new Date(Math.ceil(Date.now() / 3_600_000) * 3_600_000), zone);
		const end = instantToZoned(new Date(Math.ceil(Date.now() / 3_600_000) * 3_600_000 + 8 * 3_600_000), zone);
		overrides = [...overrides, { channelId: '', startsLocal: start, endsLocal: end }];
	}

	function channelLabel(id: string): string {
		return channelOptions.find((o) => o.value === id)?.label ?? m.oncall_unknownChannel();
	}

	function moveUp(index: number): void {
		if (index === 0) return;
		const next = [...participants];
		[next[index - 1], next[index]] = [next[index], next[index - 1]];
		participants = next;
	}

	function buildRequest(): OnCallRotationRequest {
		return {
			name: name.trim(),
			description: description.trim(),
			channelIds: participants,
			shiftHours,
			startsAt: zonedToInstant(startsLocal, zone).toISOString(),
			overrides: overrides.map((o) => ({
				channelId: o.channelId,
				startsAt: zonedToInstant(o.startsLocal, zone).toISOString(),
				endsAt: zonedToInstant(o.endsLocal, zone).toISOString()
			}))
		};
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && oncall.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.oncall_titleEdit() : m.oncall_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.oncall_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.oncall_participantsLabel()}</span>
				{#each participants as participant, index (index)}
					<div class="flex items-center gap-1">
						<span class="text-muted-foreground w-5 text-xs">{index + 1}.</span>
						<Select.Root
							type="single"
							value={participant}
							onValueChange={(v) => (participants = participants.map((p, i) => (i === index ? v : p)))}
							onOpenChange={(isOpen) => isOpen && void channels.load()}
						>
							<Select.Trigger class="min-w-0 flex-1">{participant ? channelLabel(participant) : m.oncall_choosePlaceholder()}</Select.Trigger>
							<Select.Content>
								{#each channelOptions as option (option.value)}
									<Select.Item value={option.value} label={option.label} />
								{/each}
							</Select.Content>
						</Select.Root>
						<Button variant="ghost" size="icon-sm" title={m.oncall_moveUp()} disabled={index === 0} onclick={() => moveUp(index)}>
							<ArrowUpIcon />
						</Button>
						<Button
							variant="ghost"
							size="icon-sm"
							title={m.oncall_remove()}
							disabled={participants.length === 1}
							onclick={() => (participants = participants.filter((_, i) => i !== index))}
						>
							<XIcon />
						</Button>
					</div>
				{/each}
				<div>
					<Button variant="outline" size="sm" onclick={() => (participants = [...participants, ''])}>
						<PlusIcon data-icon="inline-start" />
						{m.oncall_addParticipant()}
					</Button>
				</div>
				<span class="text-muted-foreground text-xs">{m.oncall_participantsHint()}</span>
				{#if !participantsValid}
					<span class="text-destructive text-xs">{m.oncall_errorParticipants()}</span>
				{/if}
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.oncall_shiftLabel()}</span>
				<div class="flex items-center gap-2">
					<Input type="number" min="1" max={MAX_SHIFT_HOURS} step="1" bind:value={shiftHoursText} class="w-28" aria-invalid={!shiftValid} />
					<Button variant="outline" size="sm" onclick={() => (shiftHoursText = '24')}>{m.oncall_presetDaily()}</Button>
					<Button variant="outline" size="sm" onclick={() => (shiftHoursText = '168')}>{m.oncall_presetWeekly()}</Button>
				</div>
				{#if !shiftValid}
					<span class="text-destructive text-xs">{m.oncall_errorShift()}</span>
				{/if}
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.oncall_startsLabel()}</span>
				<Input type="datetime-local" bind:value={startsLocal} class="w-60" />
				<span class="text-muted-foreground text-xs">{m.oncall_startsHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.oncall_overridesLabel()}</span>
				{#each overrides as override, index (index)}
					<div class="flex flex-wrap items-center gap-1">
						<Select.Root
							type="single"
							value={override.channelId}
							onValueChange={(v) => updateOverride(index, { channelId: v })}
							onOpenChange={(isOpen) => isOpen && void channels.load()}
						>
							<Select.Trigger class="min-w-0 flex-1">{override.channelId ? channelLabel(override.channelId) : m.oncall_choosePlaceholder()}</Select.Trigger>
							<Select.Content>
								{#each channelOptions as option (option.value)}
									<Select.Item value={option.value} label={option.label} />
								{/each}
							</Select.Content>
						</Select.Root>
						<Button variant="ghost" size="icon-sm" title={m.oncall_remove()} onclick={() => (overrides = overrides.filter((_, i) => i !== index))}>
							<XIcon />
						</Button>
						<Input type="datetime-local" value={override.startsLocal} oninput={(e) => updateOverride(index, { startsLocal: e.currentTarget.value })} class="w-52" />
						<span class="text-muted-foreground text-xs">→</span>
						<Input type="datetime-local" value={override.endsLocal} oninput={(e) => updateOverride(index, { endsLocal: e.currentTarget.value })} class="w-52" />
					</div>
				{/each}
				<div>
					<Button variant="outline" size="sm" onclick={addOverride}>
						<PlusIcon data-icon="inline-start" />
						{m.oncall_addOverride()}
					</Button>
				</div>
				<span class="text-muted-foreground text-xs">{m.oncall_overridesHint()}</span>
				{#if !overridesValid}
					<span class="text-destructive text-xs">{m.oncall_errorOverrides()}</span>
				{/if}
			</div>

			{#if oncall.saveError}
				<p class="text-destructive text-xs">{oncall.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => oncall.closeForm()}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={() => oncall.save(buildRequest())} disabled={!canSave || oncall.saving}>
				{#if oncall.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.oncall_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
