<script lang="ts">
	// Scheduled report schedules for one dashboard (ADR-0142): list, create/edit, send now, run history.
	// Self-contained - it talks to the schedules API directly rather than through a shared context, since
	// nothing outside this dialog reads them.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import {
		createDashboardSchedule,
		deleteDashboardSchedule,
		listDashboardReportRuns,
		listDashboardSchedules,
		sendDashboardScheduleNow,
		updateDashboardSchedule,
		type DashboardReportRun,
		type DashboardSchedule,
		type DashboardScheduleRequest,
		type ReportFormat
	} from '$lib/dashboard-schedules-api';
	import { CRON_PRESETS, SCHEDULE_RANGES, looksLikeCron, presetForCron, splitViewerSearch } from '$lib/dashboards/schedules';
	import { buildDashboardUrlSearch } from '$lib/dashboards/url-state';
	import { presetLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import { formatDateTime } from '$lib/time/format';
	import { browserTimeZone, timeZoneOptions } from '$lib/time/time-zone';
	import type { DashboardViewerState } from '$lib/dashboards/viewer.svelte';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import SendIcon from '@lucide/svelte/icons/send';
	import HistoryIcon from '@lucide/svelte/icons/history';

	let { open = $bindable(), viewer }: { open: boolean; viewer: DashboardViewerState } = $props();

	const dashboardId = $derived(viewer.dashboard?.id ?? '');
	const zones = timeZoneOptions();
	const RANGE_DEFAULT = '__default__';

	let schedules = $state<DashboardSchedule[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let notice = $state<string | null>(null);
	/** `null` shows the list; `'new'` or a schedule shows the form. */
	let editing = $state<DashboardSchedule | 'new' | null>(null);
	let runsFor = $state<DashboardSchedule | null>(null);
	let runs = $state<DashboardReportRun[]>([]);
	let saving = $state(false);

	let name = $state('');
	let cron = $state(CRON_PRESETS[1].cron);
	let timeZone = $state(browserTimeZone());
	let recipients = $state('');
	let timeRange = $state('');
	let variableQuery = $state('');
	let format = $state<ReportFormat>('pdf');
	let enabled = $state(true);

	$effect(() => {
		if (open && dashboardId) void reload();
		else {
			editing = null;
			runsFor = null;
			notice = null;
		}
	});

	async function reload(): Promise<void> {
		loading = true;
		error = null;
		try {
			schedules = await listDashboardSchedules(dashboardId);
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	}

	function startEdit(target: DashboardSchedule | 'new'): void {
		editing = target;
		error = null;
		notice = null;
		if (target === 'new') {
			name = viewer.dashboard?.name ?? '';
			cron = CRON_PRESETS[1].cron;
			timeZone = browserTimeZone();
			recipients = '';
			enabled = true;
			format = 'pdf';
			useCurrentView();
		} else {
			name = target.name;
			cron = target.cron;
			timeZone = target.timeZone;
			recipients = target.recipients;
			timeRange = target.timeRange;
			variableQuery = target.variableQuery;
			format = target.format;
			enabled = target.enabled;
		}
	}

	/** Captures the viewer's current range override and variable selections into the form. */
	function useCurrentView(): void {
		const search = buildDashboardUrlSearch(new URLSearchParams(), viewer.timeRangeOverride, viewer.variables, viewer.variableValues, null);
		({ timeRange, variableQuery } = splitViewerSearch(search));
	}

	const canSave = $derived(name.trim() !== '' && looksLikeCron(cron) && zones.includes(timeZone) && recipients.trim() !== '');

	async function save(): Promise<void> {
		if (!editing) return;
		saving = true;
		error = null;
		const request: DashboardScheduleRequest = { name: name.trim(), enabled, cron: cron.trim(), timeZone, recipients: recipients.trim(), timeRange, variableQuery, format };
		try {
			if (editing === 'new') await createDashboardSchedule(dashboardId, request);
			else await updateDashboardSchedule(editing.id, request);
			editing = null;
			await reload();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			saving = false;
		}
	}

	async function remove(schedule: DashboardSchedule): Promise<void> {
		if (!confirm(m.schedule_confirmDelete({ name: schedule.name }))) return;
		try {
			await deleteDashboardSchedule(schedule.id);
			await reload();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	}

	async function sendNow(schedule: DashboardSchedule): Promise<void> {
		error = null;
		try {
			await sendDashboardScheduleNow(schedule.id);
			notice = m.schedule_sendNowQueued({ name: schedule.name });
			await reload();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	}

	async function showRuns(schedule: DashboardSchedule): Promise<void> {
		runsFor = schedule;
		runs = [];
		try {
			runs = await listDashboardReportRuns(schedule.id);
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	}

	function cadenceLabel(value: string): string {
		const preset = presetForCron(value);
		return preset ? m[`schedule_preset_${preset.id}`]() : value;
	}

	function formatSize(bytes: number): string {
		return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{m.schedule_title()}</Dialog.Title>
			<Dialog.Description>{m.schedule_description()}</Dialog.Description>
		</Dialog.Header>

		{#if error}
			<p class="text-destructive text-sm">{error}</p>
		{/if}
		{#if notice}
			<p class="text-muted-foreground text-sm">{notice}</p>
		{/if}

		{#if editing}
			<div class="flex flex-col gap-3">
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.schedule_nameLabel()}</span>
					<Input bind:value={name} />
				</div>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.schedule_cadenceLabel()}</span>
					<div class="flex flex-wrap gap-1">
						{#each CRON_PRESETS as preset (preset.id)}
							<Button variant={cron.trim() === preset.cron ? 'secondary' : 'outline'} size="sm" onclick={() => (cron = preset.cron)}>
								{m[`schedule_preset_${preset.id}`]()}
							</Button>
						{/each}
					</div>
					<Input bind:value={cron} class="font-mono" aria-invalid={!looksLikeCron(cron)} />
					<span class="text-muted-foreground text-xs">{m.schedule_cronHint()}</span>
				</div>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.schedule_zoneLabel()}</span>
					<Input bind:value={timeZone} list="schedule-time-zones" aria-invalid={!zones.includes(timeZone)} />
					<datalist id="schedule-time-zones">
						{#each zones as z (z)}
							<option value={z}></option>
						{/each}
					</datalist>
				</div>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.schedule_recipientsLabel()}</span>
					<Input bind:value={recipients} placeholder="team@example.com, lead@example.com" />
					<span class="text-muted-foreground text-xs">{m.schedule_recipientsHint()}</span>
				</div>

				<div class="flex flex-wrap items-end gap-3">
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.schedule_rangeLabel()}</span>
						<Select.Root type="single" value={timeRange === '' ? RANGE_DEFAULT : timeRange} onValueChange={(v) => (timeRange = v === RANGE_DEFAULT ? '' : v)}>
							<Select.Trigger class="w-44">{timeRange === '' ? m.schedule_rangeDefault() : presetLabel(timeRange as TimeRangePreset)}</Select.Trigger>
							<Select.Content>
								<Select.Item value={RANGE_DEFAULT} label={m.schedule_rangeDefault()} />
								{#each SCHEDULE_RANGES as range (range)}
									<Select.Item value={range} label={presetLabel(range as TimeRangePreset)} />
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.schedule_formatLabel()}</span>
						<Select.Root type="single" value={format} onValueChange={(v) => (format = v as ReportFormat)}>
							<Select.Trigger class="w-28">{format.toUpperCase()}</Select.Trigger>
							<Select.Content>
								<Select.Item value="pdf" label="PDF" />
								<Select.Item value="png" label="PNG" />
							</Select.Content>
						</Select.Root>
					</div>
					<Button variant="outline" size="sm" onclick={useCurrentView}>{m.schedule_useCurrentView()}</Button>
				</div>
				<span class="text-muted-foreground text-xs">
					{variableQuery === '' ? m.schedule_variablesDefault() : m.schedule_variablesSet({ query: decodeURIComponent(variableQuery) })}
				</span>

				<label class="flex items-center gap-2 text-sm">
					<input type="checkbox" bind:checked={enabled} />
					{m.schedule_enabledLabel()}
				</label>
			</div>
			<Dialog.Footer>
				<Button variant="outline" onclick={() => (editing = null)}>{m.schedule_cancel()}</Button>
				<Button disabled={!canSave || saving} onclick={save}>
					{#if saving}<Spinner />{/if}
					{m.schedule_save()}
				</Button>
			</Dialog.Footer>
		{:else if runsFor}
			<div class="flex items-center justify-between">
				<span class="text-sm font-medium">{m.schedule_runsTitle({ name: runsFor.name })}</span>
				<Button variant="outline" size="sm" onclick={() => (runsFor = null)}>{m.schedule_back()}</Button>
			</div>
			{#if runs.length === 0}
				<p class="text-muted-foreground text-sm">{m.schedule_runsEmpty()}</p>
			{:else}
				<ul class="flex flex-col divide-y rounded-md border text-sm">
					{#each runs as run (run.id)}
						<li class="flex flex-col gap-0.5 px-3 py-2">
							<div class="flex flex-wrap items-center gap-2">
								<span class={run.status === 'Succeeded' ? 'font-medium text-green-600' : 'text-destructive font-medium'}>
									{run.status === 'Succeeded' ? m.schedule_runSucceeded() : m.schedule_runFailed()}
								</span>
								<span class="text-muted-foreground text-xs">{formatDateTime(new Date(run.startedAt))}</span>
								<span class="text-muted-foreground text-xs">{(run.durationMs / 1000).toFixed(1)} s</span>
								{#if run.status === 'Succeeded'}
									<span class="text-muted-foreground text-xs">{formatSize(run.sizeBytes)} · {m.schedule_runRecipients({ count: run.recipientCount })}</span>
								{/if}
							</div>
							{#if run.error}
								<span class="text-destructive text-xs break-words">{run.error}</span>
							{/if}
						</li>
					{/each}
				</ul>
			{/if}
		{:else}
			{#if loading}
				<div class="flex justify-center py-6"><Spinner /></div>
			{:else if schedules.length === 0}
				<p class="text-muted-foreground text-sm">{m.schedule_empty()}</p>
			{:else}
				<ul class="flex flex-col divide-y rounded-md border text-sm">
					{#each schedules as schedule (schedule.id)}
						<li class="flex flex-wrap items-center gap-2 px-3 py-2">
							<div class="min-w-0 flex-1">
								<div class="truncate font-medium">{schedule.name}{schedule.enabled ? '' : ` (${m.schedule_paused()})`}</div>
								<div class="text-muted-foreground truncate text-xs">
									{cadenceLabel(schedule.cron)} · {schedule.timeZone} · {schedule.format.toUpperCase()} · {schedule.recipients}
								</div>
								{#if schedule.enabled && new Date(schedule.nextRunAt).getFullYear() < 2100}
									<div class="text-muted-foreground text-xs">{m.schedule_nextRun({ time: formatDateTime(new Date(schedule.nextRunAt)) })}</div>
								{/if}
							</div>
							<Button variant="ghost" size="icon-sm" title={m.schedule_sendNow()} onclick={() => sendNow(schedule)}><SendIcon /></Button>
							<Button variant="ghost" size="icon-sm" title={m.schedule_runs()} onclick={() => showRuns(schedule)}><HistoryIcon /></Button>
							<Button variant="ghost" size="icon-sm" title={m.schedule_edit()} onclick={() => startEdit(schedule)}><PencilIcon /></Button>
							<Button variant="ghost" size="icon-sm" title={m.schedule_delete()} onclick={() => remove(schedule)}><Trash2Icon /></Button>
						</li>
					{/each}
				</ul>
			{/if}
			<Dialog.Footer>
				<Button onclick={() => startEdit('new')}>
					<PlusIcon data-icon="inline-start" />
					{m.schedule_new()}
				</Button>
			</Dialog.Footer>
		{/if}
	</Dialog.Content>
</Dialog.Root>
