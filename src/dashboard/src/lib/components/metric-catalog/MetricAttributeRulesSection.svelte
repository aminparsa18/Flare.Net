<script lang="ts">
	// "Reduce attributes" for one metric (ADR-0083): tick the high-cardinality data-point
	// attributes, pick drop / keep-only, save a rule that `Flare.Ingest` applies from the next
	// rule refresh on. Lists the existing rules covering this metric (exact or prefix) so they
	// can be toggled or removed here. The rule list is local state - small, one panel, no need
	// to thread it through the catalog state.
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Switch } from '$lib/components/ui/switch';
	import { Badge } from '$lib/components/ui/badge';
	import TrashIcon from '@lucide/svelte/icons/trash-2';
	import {
		createMetricAttributeRule,
		deleteMetricAttributeRule,
		listMetricAttributeRules,
		previewMetricAttributeRule,
		type MetricAttributeRulePreview,
		ruleMatchesMetric,
		updateMetricAttributeRule,
		type MetricAttributeRule,
		type MetricAttributeRuleMode
	} from '$lib/metric-attribute-rules-api';
	import { formatCount } from '$lib/ingestion/format';
	import { cardinalityClass } from './format';
	import * as m from '$lib/paraglide/messages';

	let {
		metricName,
		attributes,
		canMutate
	}: {
		metricName: string;
		attributes: { key: string; distinctValueCount: number }[];
		canMutate: boolean;
	} = $props();

	let rules = $state<MetricAttributeRule[]>([]);
	let selected = $state<string[]>([]);
	let mode = $state<MetricAttributeRuleMode>('Drop');
	let busy = $state(false);
	let error = $state<string | null>(null);
	let preview = $state<MetricAttributeRulePreview | null>(null);

	// A preview describes one draft; any change to the draft invalidates it.
	$effect(() => {
		void selected.length;
		void mode;
		void metricName;
		preview = null;
	});

	const covering = $derived(rules.filter((r) => ruleMatchesMetric(r, metricName)));

	// Reload and reset the form whenever the inspected metric changes.
	$effect(() => {
		const name = metricName;
		selected = [];
		mode = 'Drop';
		error = null;
		listMetricAttributeRules().then(
			(all) => {
				if (name === metricName) rules = all;
			},
			(err) => (error = err instanceof Error ? err.message : String(err))
		);
	});

	async function run(action: () => Promise<void>): Promise<void> {
		busy = true;
		error = null;
		try {
			await action();
			rules = await listMetricAttributeRules();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}

	function toggle(key: string, checked: boolean): void {
		selected = checked ? [...selected, key] : selected.filter((k) => k !== key);
	}

	const runPreview = () =>
		run(async () => {
			preview = await previewMetricAttributeRule({ metricName, mode, attributes: selected });
		});

	const create = () =>
		run(async () => {
			await createMetricAttributeRule({
				name: m.metricAttrRules_defaultName({ metric: metricName }),
				metricName,
				mode,
				attributes: selected
			});
			selected = [];
		});

	const setEnabled = (rule: MetricAttributeRule, enabled: boolean) =>
		run(async () => {
			await updateMetricAttributeRule(rule.id, { ...rule, enabled });
		});

	const remove = (rule: MetricAttributeRule) => run(() => deleteMetricAttributeRule(rule.id));
</script>

<section class="space-y-3">
	<div>
		<h3 class="text-sm font-medium">{m.metricAttrRules_heading()}</h3>
		<p class="text-muted-foreground mt-1 text-xs">{m.metricAttrRules_hint()}</p>
	</div>

	{#if covering.length > 0}
		<ul class="divide-y rounded-md border">
			{#each covering as rule (rule.id)}
				<li class="flex flex-wrap items-center gap-2 px-3 py-2 text-sm">
					<Badge variant="outline">{rule.mode === 'Drop' ? m.metricAttrRules_modeDrop() : m.metricAttrRules_modeKeepOnly()}</Badge>
					<span class="font-mono text-xs break-all">{rule.attributes.join(', ')}</span>
					{#if rule.metricName !== metricName}
						<span class="text-muted-foreground text-xs">{m.metricAttrRules_viaPattern({ pattern: rule.metricName })}</span>
					{/if}
					{#if canMutate}
						<Switch class="ml-auto" checked={rule.enabled} disabled={busy} onCheckedChange={(v) => setEnabled(rule, v)} aria-label={m.metricAttrRules_enabled()} />
						<Button variant="ghost" size="icon-sm" disabled={busy} onclick={() => remove(rule)} aria-label={m.metricAttrRules_delete()}>
							<TrashIcon />
						</Button>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}

	{#if canMutate}
		<div class="space-y-2 rounded-md border p-3">
			<div class="flex flex-wrap gap-x-4 gap-y-2">
				{#each attributes as attribute (attribute.key)}
					<label class="flex items-center gap-2 text-sm">
						<Checkbox checked={selected.includes(attribute.key)} onCheckedChange={(v) => toggle(attribute.key, v === true)} />
						<span class="font-mono text-xs">{attribute.key}</span>
						<span class="text-xs tabular-nums {cardinalityClass(attribute.distinctValueCount)}">{formatCount(attribute.distinctValueCount)}</span>
					</label>
				{/each}
			</div>
			<div class="flex flex-wrap items-center gap-2">
				<div class="flex rounded-md border p-0.5" role="radiogroup">
					{#each [{ value: 'Drop', label: m.metricAttrRules_modeDrop() }, { value: 'KeepOnly', label: m.metricAttrRules_modeKeepOnly() }] as const as option (option.value)}
						<Button variant={mode === option.value ? 'secondary' : 'ghost'} size="sm" class="h-7" role="radio" aria-checked={mode === option.value} onclick={() => (mode = option.value)}>
							{option.label}
						</Button>
					{/each}
				</div>
				<Button variant="outline" size="sm" disabled={busy || selected.length === 0} onclick={runPreview}>{m.metricAttrRules_preview()}</Button>
				<Button size="sm" disabled={busy || selected.length === 0} onclick={create}>{m.metricAttrRules_create()}</Button>
			</div>
			{#if preview}
				<div class="rounded-md bg-muted/50 p-2 text-xs">
					{#if preview.metrics.length === 0}
						<p>{m.metricAttrRules_previewEmpty({ hours: Math.round(preview.windowMinutes / 60) })}</p>
					{:else}
						<p class="font-medium">
							{m.metricAttrRules_previewTotal({ before: formatCount(preview.seriesBefore), after: formatCount(preview.seriesAfter) })}
						</p>
						{#if preview.metrics.length > 1}
							<ul class="mt-1 space-y-0.5">
								{#each preview.metrics as row (row.metricName)}
									<li class="flex justify-between gap-2">
										<span class="font-mono break-all">{row.metricName}</span>
										<span class="tabular-nums">{formatCount(row.seriesBefore)} → {formatCount(row.seriesAfter)}</span>
									</li>
								{/each}
							</ul>
						{/if}
						{#if preview.truncated}
							<p class="text-muted-foreground mt-1">{m.metricAttrRules_previewTruncated()}</p>
						{/if}
						<p class="text-muted-foreground mt-1">{m.metricAttrRules_previewCaveat({ hours: Math.round(preview.windowMinutes / 60) })}</p>
					{/if}
				</div>
			{/if}
			<p class="text-muted-foreground text-xs">{m.metricAttrRules_irreversible()}</p>
		</div>
	{/if}

	{#if error}
		<p class="text-destructive text-sm">{error}</p>
	{/if}
</section>
