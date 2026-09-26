<script lang="ts">
	// Create/edit a maintenance window. Same Dialog + reset-on-open $effect shape as
	// NotificationChannelFormDialog.svelte. Start/end/repeat-until are edited as wall-clock
	// times in the window's own time zone and converted to instants on save (see
	// $lib/time/time-zone.ts); the client-side checks mirror
	// MaintenanceWindowRequest.Validate so the Save button can't submit a request the API rejects.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import { alertsContext } from '$lib/alerts/context';
	import { maintenanceWindowsContext } from '$lib/maintenance-windows/context';
	import { browserTimeZone, instantToZoned, timeZoneOptions, zonedToInstant } from '$lib/time/time-zone';
	import type { MaintenanceWindowRecurrence, MaintenanceWindowRequest } from '$lib/maintenance-windows-api';
	import * as m from '$lib/paraglide/messages';

	const alerts = alertsContext.get();
	const maintenance = maintenanceWindowsContext.get();

	const open = $derived(maintenance.formTarget !== null);
	const isEdit = $derived(maintenance.formTarget !== null && maintenance.formTarget !== 'new');

	const zones = timeZoneOptions();
	// 2023-01-01 was a Sunday - index = System.DayOfWeek ordinal. Listed Monday-first.
	const weekdays = [1, 2, 3, 4, 5, 6, 0].map((day) => ({
		day,
		label: new Date(Date.UTC(2023, 0, 1 + day)).toLocaleDateString(undefined, { weekday: 'short', timeZone: 'UTC' })
	}));
	const HOUR_MS = 3_600_000;

	let name = $state('');
	let description = $state('');
	let ruleIds = $state<string[]>([]);
	let timeZone = $state('UTC');
	let startsLocal = $state('');
	let endsLocal = $state('');
	let recurrence = $state<MaintenanceWindowRecurrence>('None');
	let daysOfWeek = $state<number[]>([]);
	let repeatUntilLocal = $state('');

	$effect(() => {
		const target = maintenance.formTarget;
		if (target === 'new') {
			// Default: the next full hour, for one hour, in the browser's zone.
			const start = new Date(Math.ceil(Date.now() / HOUR_MS) * HOUR_MS);
			name = '';
			description = '';
			ruleIds = [];
			timeZone = browserTimeZone();
			startsLocal = instantToZoned(start, timeZone);
			endsLocal = instantToZoned(new Date(start.getTime() + HOUR_MS), timeZone);
			recurrence = 'None';
			daysOfWeek = [];
			repeatUntilLocal = '';
		} else if (target) {
			name = target.name;
			description = target.description;
			ruleIds = [...target.ruleIds];
			timeZone = target.timeZone;
			startsLocal = instantToZoned(new Date(target.startsAt), target.timeZone);
			endsLocal = instantToZoned(new Date(target.endsAt), target.timeZone);
			recurrence = target.recurrence;
			daysOfWeek = [...target.daysOfWeek];
			repeatUntilLocal = target.repeatUntil ? instantToZoned(new Date(target.repeatUntil), target.timeZone) : '';
		}
	});

	const ruleOptions = $derived(alerts.rules.map((r) => ({ value: r.id, label: r.name })));
	const validZone = $derived(zones.includes(timeZone));

	const error = $derived.by((): string | null => {
		if (!validZone) return m.maintenanceWindowForm_errorTimeZone();
		if (!startsLocal || !endsLocal) return null;
		const duration = zonedToInstant(endsLocal, timeZone).getTime() - zonedToInstant(startsLocal, timeZone).getTime();
		if (duration <= 0) return m.maintenanceWindowForm_errorEndBeforeStart();
		if (recurrence === 'Daily' && duration > 24 * HOUR_MS) return m.maintenanceWindowForm_errorTooLongDaily();
		if (recurrence === 'Weekly' && duration > 7 * 24 * HOUR_MS) return m.maintenanceWindowForm_errorTooLongWeekly();
		if (recurrence === 'Weekly' && daysOfWeek.length === 0) return m.maintenanceWindowForm_errorNoDays();
		if (recurrence !== 'None' && repeatUntilLocal && repeatUntilLocal <= startsLocal) return m.maintenanceWindowForm_errorRepeatUntil();
		return null;
	});

	const canSave = $derived(name.trim().length > 0 && startsLocal !== '' && endsLocal !== '' && error === null);

	function toggleDay(day: number): void {
		daysOfWeek = daysOfWeek.includes(day) ? daysOfWeek.filter((d) => d !== day) : [...daysOfWeek, day];
	}

	function buildRequest(): MaintenanceWindowRequest {
		return {
			name: name.trim(),
			description: description.trim(),
			ruleIds,
			startsAt: zonedToInstant(startsLocal, timeZone).toISOString(),
			endsAt: zonedToInstant(endsLocal, timeZone).toISOString(),
			recurrence,
			daysOfWeek: recurrence === 'Weekly' ? [...daysOfWeek].sort((a, b) => a - b) : [],
			repeatUntil: recurrence !== 'None' && repeatUntilLocal ? zonedToInstant(repeatUntilLocal, timeZone).toISOString() : null,
			timeZone
		};
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && maintenance.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.maintenanceWindowForm_titleEdit() : m.maintenanceWindowForm_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.maintenanceWindowForm_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.maintenanceWindowForm_rulesLabel()}</span>
				<div>
					<PopoverMultiSelect
						label={ruleIds.length === 0 ? m.maintenanceWindowTable_allRules() : m.maintenanceWindowForm_rulesLabel()}
						options={ruleOptions}
						selected={ruleIds}
						onChange={(next) => (ruleIds = next)}
					/>
				</div>
				<span class="text-muted-foreground text-xs">{m.maintenanceWindowForm_rulesHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.maintenanceWindowForm_timeZoneLabel()}</span>
				<Input bind:value={timeZone} list="maintenance-window-time-zones" aria-invalid={!validZone} />
				<datalist id="maintenance-window-time-zones">
					{#each zones as zone (zone)}
						<option value={zone}></option>
					{/each}
				</datalist>
				<span class="text-muted-foreground text-xs">{m.maintenanceWindowForm_timeZoneHint()}</span>
			</div>

			<div class="grid grid-cols-2 gap-2">
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.maintenanceWindowForm_startsLabel()}</span>
					<Input type="datetime-local" bind:value={startsLocal} />
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.maintenanceWindowForm_endsLabel()}</span>
					<Input type="datetime-local" bind:value={endsLocal} />
				</div>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.maintenanceWindowForm_repeatLabel()}</span>
				<Select.Root type="single" value={recurrence} onValueChange={(v) => v && (recurrence = v as MaintenanceWindowRecurrence)}>
					<Select.Trigger class="w-48">
						{recurrence === 'Daily'
							? m.maintenanceWindowForm_repeatDaily()
							: recurrence === 'Weekly'
								? m.maintenanceWindowForm_repeatWeekly()
								: m.maintenanceWindowForm_repeatNone()}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="None" label={m.maintenanceWindowForm_repeatNone()} />
						<Select.Item value="Daily" label={m.maintenanceWindowForm_repeatDaily()} />
						<Select.Item value="Weekly" label={m.maintenanceWindowForm_repeatWeekly()} />
					</Select.Content>
				</Select.Root>
				{#if recurrence !== 'None'}
					<span class="text-muted-foreground text-xs">{m.maintenanceWindowForm_recurringHint()}</span>
				{/if}
			</div>

			{#if recurrence === 'Weekly'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.maintenanceWindowForm_daysLabel()}</span>
					<div class="flex flex-wrap gap-1">
						{#each weekdays as { day, label } (day)}
							<Button
								variant={daysOfWeek.includes(day) ? 'secondary' : 'outline'}
								size="sm"
								aria-pressed={daysOfWeek.includes(day)}
								onclick={() => toggleDay(day)}
							>
								{label}
							</Button>
						{/each}
					</div>
				</div>
			{/if}

			{#if recurrence !== 'None'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.maintenanceWindowForm_repeatUntilLabel()}</span>
					<Input type="datetime-local" bind:value={repeatUntilLocal} class="w-60" />
					<span class="text-muted-foreground text-xs">{m.maintenanceWindowForm_repeatUntilHint()}</span>
				</div>
			{/if}

			{#if error}
				<p class="text-destructive text-xs">{error}</p>
			{/if}
			{#if maintenance.saveError}
				<p class="text-destructive text-xs">{maintenance.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => maintenance.closeForm()}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={() => maintenance.save(buildRequest())} disabled={!canSave || maintenance.saving}>
				{#if maintenance.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.maintenanceWindowForm_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
