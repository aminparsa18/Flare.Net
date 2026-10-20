<script lang="ts">
	// Create/edit a check-in monitor. Same Dialog + reset-on-open $effect shape as SyntheticMonitorFormDialog.svelte.
	// Validation mirrors CheckInMonitorRequest.Validate server-side.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import * as Select from '#lib/components/ui/select/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import { Textarea } from '#lib/components/ui/textarea/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { checkInMonitorsContext } from '#lib/checkin-monitors/context.ts';
	import type { CheckInMonitorRequest } from '#lib/checkin-monitors-api.ts';
	import * as m from '#lib/paraglide/messages.js';

	const monitors = checkInMonitorsContext.get();

	const open = $derived(monitors.formTarget !== null);
	const isEdit = $derived(monitors.formTarget !== null && monitors.formTarget !== 'new');

	const MIN_INTERVAL = 60;
	const MAX_INTERVAL = 2_678_400;
	const MAX_SECONDS = 604_800;

	type ScheduleKind = 'cron' | 'interval';

	let name = $state('');
	let slug = $state('');
	let description = $state('');
	let scheduleKind = $state<ScheduleKind>('cron');
	let cronExpression = $state('0 2 * * *');
	let timeZone = $state('UTC');
	let intervalText = $state('3600');
	let graceText = $state('300');
	let maxRuntimeText = $state('0');
	let enabled = $state(true);

	$effect(() => {
		const t = monitors.formTarget;
		if (t === 'new') {
			name = '';
			slug = '';
			description = '';
			scheduleKind = 'cron';
			cronExpression = '0 2 * * *';
			timeZone = 'UTC';
			intervalText = '3600';
			graceText = '300';
			maxRuntimeText = '0';
			enabled = true;
		} else if (t) {
			name = t.name;
			slug = t.slug;
			description = t.description;
			scheduleKind = t.cronExpression ? 'cron' : 'interval';
			cronExpression = t.cronExpression || '0 2 * * *';
			timeZone = t.timeZone || 'UTC';
			intervalText = String(t.intervalSeconds || 3600);
			graceText = String(t.graceSeconds);
			maxRuntimeText = String(t.maxRuntimeSeconds);
			enabled = t.enabled;
		}
	});

	const interval = $derived(Number(intervalText));
	const grace = $derived(Number(graceText));
	const maxRuntime = $derived(Number(maxRuntimeText));
	const slugValid = $derived(isEdit || slug.trim() === '' || /^[a-z0-9][a-z0-9_-]{0,63}$/.test(slug.trim()));
	// The server parses the expression for real; five whitespace-separated fields or an @macro catches typos early.
	const cronValid = $derived(/^@[a-z]+$/i.test(cronExpression.trim()) || cronExpression.trim().split(/\s+/).length === 5);
	const timeZoneValid = $derived.by(() => {
		try {
			new Intl.DateTimeFormat(undefined, { timeZone: timeZone.trim() });
			return timeZone.trim().length > 0;
		} catch {
			return false;
		}
	});
	const intervalValid = $derived(Number.isInteger(interval) && interval >= MIN_INTERVAL && interval <= MAX_INTERVAL);
	const graceValid = $derived(Number.isInteger(grace) && grace >= 0 && grace <= MAX_SECONDS);
	const maxRuntimeValid = $derived(Number.isInteger(maxRuntime) && maxRuntime >= 0 && maxRuntime <= MAX_SECONDS);
	const scheduleValid = $derived(scheduleKind === 'cron' ? cronValid && timeZoneValid : intervalValid);
	const canSave = $derived(name.trim().length > 0 && slugValid && scheduleValid && graceValid && maxRuntimeValid);

	function buildRequest(): CheckInMonitorRequest {
		return {
			name: name.trim(),
			slug: isEdit ? undefined : slug.trim() || undefined,
			description: description.trim(),
			enabled,
			cronExpression: scheduleKind === 'cron' ? cronExpression.trim() : undefined,
			intervalSeconds: scheduleKind === 'interval' ? interval : undefined,
			timeZone: scheduleKind === 'cron' ? timeZone.trim() : undefined,
			graceSeconds: grace,
			maxRuntimeSeconds: maxRuntime
		};
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && monitors.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.checkin_titleEdit() : m.checkin_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.checkin_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.checkin_slugLabel()}</span>
				<Input bind:value={slug} disabled={isEdit} placeholder="nightly-backup" class="font-mono" aria-invalid={!slugValid} />
				<span class="text-xs {slugValid ? 'text-muted-foreground' : 'text-destructive'}">
					{slugValid ? m.checkin_slugHint() : m.checkin_slugError()}
				</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.checkin_scheduleLabel()}</span>
				<Select.Root type="single" value={scheduleKind} onValueChange={(v) => (scheduleKind = v as ScheduleKind)}>
					<Select.Trigger class="w-48">
						{scheduleKind === 'cron' ? m.checkin_scheduleCron() : m.checkin_scheduleInterval()}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="cron" label={m.checkin_scheduleCron()} />
						<Select.Item value="interval" label={m.checkin_scheduleInterval()} />
					</Select.Content>
				</Select.Root>
			</div>

			{#if scheduleKind === 'cron'}
				<div class="flex gap-3">
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.checkin_cronLabel()}</span>
						<Input bind:value={cronExpression} placeholder="0 2 * * *" class="font-mono" aria-invalid={!cronValid} />
					</div>
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.checkin_timeZoneLabel()}</span>
						<Input bind:value={timeZone} placeholder="UTC" class="font-mono" aria-invalid={!timeZoneValid} />
					</div>
				</div>
				<span class="text-xs {cronValid && timeZoneValid ? 'text-muted-foreground' : 'text-destructive'}">
					{cronValid && timeZoneValid ? m.checkin_cronHint() : m.checkin_cronError()}
				</span>
			{:else}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.checkin_intervalLabel()}</span>
					<Input type="number" min={MIN_INTERVAL} max={MAX_INTERVAL} step="1" bind:value={intervalText} class="w-36" aria-invalid={!intervalValid} />
					<span class="text-xs {intervalValid ? 'text-muted-foreground' : 'text-destructive'}">
						{intervalValid ? m.checkin_intervalHint() : m.checkin_intervalError()}
					</span>
				</div>
			{/if}

			<div class="flex gap-3">
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.checkin_graceLabel()}</span>
					<Input type="number" min="0" max={MAX_SECONDS} step="1" bind:value={graceText} class="w-36" aria-invalid={!graceValid} />
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.checkin_maxRuntimeLabel()}</span>
					<Input type="number" min="0" max={MAX_SECONDS} step="1" bind:value={maxRuntimeText} class="w-36" aria-invalid={!maxRuntimeValid} />
				</div>
			</div>
			<span class="text-muted-foreground -mt-2 text-xs">{m.checkin_graceHint()}</span>

			{#if monitors.saveError}
				<p class="text-destructive text-xs">{monitors.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => monitors.closeForm()}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={() => monitors.save(buildRequest())} disabled={!canSave || monitors.saving}>
				{#if monitors.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.checkin_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
