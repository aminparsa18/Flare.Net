<script lang="ts">
	// Create/edit a log-based metric (ADR-0140). Same Dialog shape as PipelineRuleFormDialog.
	// The form edits service/level/search directly; every other LogFilter field carried over
	// from the Logs explorer (attribute filters, scopes, ...) is kept untouched in `extra`.
	import { onMount } from 'svelte';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Switch } from '$lib/components/ui/switch';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Badge } from '$lib/components/ui/badge';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import { logMetricsContext } from '$lib/log-metrics/context';
	import { extraFieldNames, stripIgnored, suggestMetricName } from '$lib/log-metrics/format';
	import { MAX_GROUP_BY_KEYS, previewLogMetric, type LogMetricPreview } from '$lib/log-metrics-api';
	import { aggregateLogs, type LogFilter } from '$lib/api';
	import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';
	import { formatCount } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';
	import XIcon from '@lucide/svelte/icons/x';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';

	const logMetrics = logMetricsContext.get();

	const open = $derived(logMetrics.formTarget !== null);
	const editing = $derived(logMetrics.formTarget !== null && logMetrics.formTarget !== 'new' ? logMetrics.formTarget : null);

	let name = $state('');
	let description = $state('');
	let metricName = $state('');
	let metricNameTouched = $state(false);
	let enabled = $state(true);
	let services = $state<string[]>([]);
	let severityNumbers = $state<number[]>([]);
	let search = $state('');
	let extra = $state.raw<LogFilter>({});
	let groupBy = $state<string[]>([]);
	let groupInput = $state('');

	let previewing = $state(false);
	let preview = $state<LogMetricPreview | null>(null);
	let previewError = $state<string | null>(null);

	function load(condition: LogFilter): void {
		const { services: s, severityNumbers: n, search: q, ...rest } = stripIgnored(condition);
		services = s ? [...s] : [];
		severityNumbers = n ? [...n] : [];
		search = q ?? '';
		extra = rest;
	}

	// Resets the draft whenever the dialog opens for a different target.
	$effect(() => {
		const target = logMetrics.formTarget;
		preview = null;
		previewError = null;
		groupInput = '';
		if (target === 'new') {
			name = '';
			description = '';
			metricName = '';
			metricNameTouched = false;
			enabled = true;
			groupBy = [];
			load(logMetrics.draft ?? {});
		} else if (target) {
			name = target.name;
			description = target.description;
			metricName = target.metricName;
			metricNameTouched = true;
			enabled = target.enabled;
			groupBy = [...target.groupBy];
			load(target.condition);
		}
	});

	// A preview describes one draft; any change to it invalidates the result.
	$effect(() => {
		void [services, severityNumbers, search, extra, groupBy];
		preview = null;
		previewError = null;
	});

	let knownServices = $state<string[]>([]);
	onMount(() => {
		void loadKnownServices();
	});
	async function loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await aggregateLogs({
				filter: { from: from.toISOString(), to: to.toISOString() },
				bucketWidthSeconds: 7 * 24 * 60 * 60,
				groupBy: 'Service'
			});
			knownServices = [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		} catch {
			// Non-critical - the picker just shows fewer options.
		}
	}

	const serviceOptions = $derived([...new Set([...knownServices, ...services])].map((s) => ({ value: s, label: s })));
	const severityOptions = $derived(SEVERITY_BUCKETS.map((b) => ({ value: b.id, label: severityBucketLabel(b) })));
	const selectedSeverityIds = $derived(
		SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severityNumbers.includes(n))).map((b) => b.id)
	);
	function handleSeverityChange(ids: string[]): void {
		const numbers = ids.flatMap((id) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.id === id);
			return bucket ? severityNumbersForBucket(bucket) : [];
		});
		severityNumbers = [...new Set(numbers)];
	}

	function buildCondition(): LogFilter {
		return {
			...extra,
			services: services.length ? [...services] : undefined,
			severityNumbers: severityNumbers.length ? [...severityNumbers] : undefined,
			search: search.trim() || undefined
		};
	}

	const extraNames = $derived(extraFieldNames(extra));
	const matchesAllLogs = $derived(
		services.length === 0 && severityNumbers.length === 0 && search.trim().length === 0 && extraNames.length === 0
	);

	function setName(value: string): void {
		name = value;
		if (!metricNameTouched) metricName = suggestMetricName(value);
	}

	function addGroupKeys(raw: string): void {
		const keys = raw
			.split(',')
			.map((k) => k.trim())
			.filter((k) => k && !groupBy.includes(k));
		groupBy = [...groupBy, ...keys].slice(0, MAX_GROUP_BY_KEYS);
		groupInput = '';
	}

	const METRIC_NAME_PATTERN = /^[A-Za-z][A-Za-z0-9_.-]{0,199}$/;
	const metricNameValid = $derived(METRIC_NAME_PATTERN.test(metricName.trim()));
	const canSave = $derived(name.trim().length > 0 && metricNameValid);

	async function handleSave(): Promise<void> {
		await logMetrics.save(editing?.id ?? null, {
			name: name.trim(),
			description: description.trim(),
			enabled,
			metricName: metricName.trim(),
			condition: buildCondition(),
			groupBy
		});
	}

	async function handlePreview(): Promise<void> {
		previewing = true;
		previewError = null;
		try {
			preview = await previewLogMetric({ condition: buildCondition(), groupBy });
		} catch (err) {
			preview = null;
			previewError = err instanceof Error ? err.message : String(err);
		} finally {
			previewing = false;
		}
	}

	function seriesLabel(serviceName: string, values: string[]): string {
		const dims = groupBy.map((key, i) => (values[i] ? `${key}=${values[i]}` : null)).filter(Boolean);
		return [serviceName, ...dims].join(' · ');
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && logMetrics.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{editing ? m.logMetricForm_titleEdit() : m.logMetricForm_titleNew()}</Dialog.Title>
			<Dialog.Description>{m.logMetricForm_description()}</Dialog.Description>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.logMetricForm_nameLabel()}</span>
				<Input value={name} oninput={(e) => setName(e.currentTarget.value)} placeholder={m.logMetricForm_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.logMetricForm_metricNameLabel()}</span>
				<Input
					value={metricName}
					oninput={(e) => {
						metricName = e.currentTarget.value;
						metricNameTouched = true;
					}}
					placeholder="logs.checkout.errors"
					class="font-mono text-xs"
					aria-invalid={metricName.trim().length > 0 && !metricNameValid}
				/>
				<span class="text-muted-foreground text-xs">{m.logMetricForm_metricNameHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.logMetricForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.logMetricForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.logMetricForm_conditionLabel()}</span>
				<div class="flex flex-wrap items-center gap-2">
					<PopoverMultiSelect label={m.logMetricForm_serviceLabel()} options={serviceOptions} selected={services} onChange={(next) => (services = next)} />
					<PopoverMultiSelect label={m.logMetricForm_levelLabel()} options={severityOptions} selected={selectedSeverityIds} onChange={handleSeverityChange} />
				</div>
				<Input bind:value={search} placeholder={m.logMetricForm_searchPlaceholder()} />
				{#if extraNames.length > 0}
					<span class="text-muted-foreground text-xs">{m.logMetricForm_carriedOver({ fields: extraNames.join(', ') })}</span>
				{/if}
				{#if matchesAllLogs}
					<div class="text-warning bg-warning/10 flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs">
						<TriangleAlertIcon class="size-3.5 shrink-0" />
						{m.logMetricForm_matchesAllLogsWarning()}
					</div>
				{/if}
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.logMetricForm_groupByLabel()}</span>
				<div class="flex flex-wrap items-center gap-1.5">
					{#each groupBy as key (key)}
						<Badge variant="secondary" class="gap-1 font-mono">
							{key}
							<button type="button" aria-label={m.logMetricForm_removeGroupKey({ key })} onclick={() => (groupBy = groupBy.filter((k) => k !== key))}>
								<XIcon class="size-3" />
							</button>
						</Badge>
					{/each}
				</div>
				{#if groupBy.length < MAX_GROUP_BY_KEYS}
					<Input
						bind:value={groupInput}
						placeholder={m.logMetricForm_groupByPlaceholder()}
						class="font-mono text-xs"
						onkeydown={(e) => {
							if (e.key === 'Enter' || e.key === ',') {
								e.preventDefault();
								addGroupKeys(groupInput);
							}
						}}
						onblur={() => groupInput.trim() && addGroupKeys(groupInput)}
					/>
				{/if}
				<span class="text-muted-foreground text-xs">{m.logMetricForm_groupByHint({ max: MAX_GROUP_BY_KEYS })}</span>
			</div>

			<div class="flex items-center gap-2">
				<Switch bind:checked={enabled} />
				<span class="text-xs">{m.logMetricForm_enabledLabel()}</span>
			</div>

			<div class="flex flex-wrap items-center gap-2 border-t pt-3">
				<Button variant="outline" size="sm" onclick={handlePreview} disabled={previewing}>
					{#if previewing}
						<Spinner class="size-3.5" />
					{/if}
					{m.logMetricForm_previewButton()}
				</Button>
				{#if preview}
					{#if preview.totalLogs === 0}
						<Badge variant="outline">{m.logMetricForm_previewNoMatches({ minutes: preview.windowMinutes })}</Badge>
					{:else}
						<Badge variant={preview.seriesCapped ? 'warning' : 'outline'}>
							{preview.seriesCapped
								? m.logMetricForm_previewSeriesCapped({ series: formatCount(preview.seriesCount) })
								: m.logMetricForm_previewSeries({ series: formatCount(preview.seriesCount) })}
						</Badge>
						<span class="text-muted-foreground text-xs">
							{m.logMetricForm_previewLogs({ logs: formatCount(preview.totalLogs), minutes: preview.windowMinutes })}
						</span>
					{/if}
				{:else if previewError}
					<span class="text-destructive text-xs">{previewError}</span>
				{/if}
			</div>

			{#if preview && preview.top.length > 0}
				<ul class="flex max-h-48 flex-col gap-1 overflow-y-auto rounded-md border p-2 text-xs">
					{#each preview.top as series, i (i)}
						<li class="flex items-center justify-between gap-2">
							<span class="truncate font-mono">{seriesLabel(series.serviceName, series.values)}</span>
							<span class="text-muted-foreground shrink-0">{formatCount(series.count)}</span>
						</li>
					{/each}
				</ul>
			{/if}

			{#if logMetrics.saveError}
				<p class="text-destructive text-xs">{logMetrics.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => logMetrics.closeForm()}>{m.logMetricForm_cancel()}</Button>
			<Button size="sm" onclick={handleSave} disabled={!canSave || logMetrics.saving}>
				{#if logMetrics.saving}
					<Spinner class="size-3.5" />
				{/if}
				{editing ? m.logMetricForm_saveChanges() : m.logMetricForm_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
