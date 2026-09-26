<script lang="ts">
	// Formula mode's query-row editor - the MetricPicker.svelte-equivalent panel for
	// MetricsExplorerState.mode === 'formula' (see that field's own remarks for why formula
	// mode is a fully separate set of state, not a MetricPicker/MetricChart variant).
	//
	// Each row picks one Gauge/Sum metric (Histogram excluded from the picker entirely - a
	// deliberate v1 scope cut, see docs-internal/adr/0036-cross-query-metric-formulas.md) and
	// an optional per-row Group by, same PopoverSingleSelect widget MetricsToolbar's own
	// (page-wide, single-mode) Group by picker uses.
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import PopoverSingleSelect from '$lib/components/logs/PopoverSingleSelect.svelte';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { MAX_FORMULA_QUERIES, type FormulaQueryDef } from '$lib/metrics/state.svelte';
	import { isHistogramType, type MetricNameInfo } from '$lib/metrics-api';
	import * as m from '$lib/paraglide/messages';

	const explorer = metricsExplorerContext.get();

	function metricKey(metric: Pick<MetricNameInfo, 'metricName' | 'serviceName'>): string {
		return `${metric.metricName}\u0000${metric.serviceName}`;
	}

	// Gauge/Sum only (see this file's header comment) - a formula operand needs one scalar
	// Value per bucket, which is exactly what MetricSeriesPoint.value is for those two types;
	// Histogram's per-bucket shape is percentiles/sum/count, not a single Value, and picking
	// which one a formula should use is a real, separate design question (docs-internal/adr/0036
	// names it as explicit follow-up scope, not something to guess a default for here).
	const eligibleMetrics = $derived(explorer.names.filter((mn) => !isHistogramType(mn.type)));
	const metricOptions = $derived(eligibleMetrics.map((mn) => ({ value: metricKey(mn), label: `${mn.metricName} · ${mn.serviceName}` })));
	const metricByKey = $derived(new Map(eligibleMetrics.map((mn) => [metricKey(mn), mn])));

	const GROUP_BY_NONE = '__none__';

	function groupByOptions(row: FormulaQueryDef) {
		return [
			{ value: GROUP_BY_NONE, label: m.metricsToolbar_groupByNone() },
			...row.attributeKeys.map((k) => ({ value: k.key, label: `${k.key} (${k.distinctValueCount})` }))
		];
	}

	// Same local-draft + debounce shape LogsToolbar.svelte's own search input uses -
	// setFormulaExpression re-parses and (if valid) re-fetches every referenced query on
	// every call, so typing "A / B" character by character would otherwise fire several
	// aborted-in-flight requests for every syntactically-invalid intermediate state.
	let expressionDraft = $state(explorer.formulaExpression);
	let expressionDebounce: ReturnType<typeof setTimeout> | undefined;

	$effect(() => {
		expressionDraft = explorer.formulaExpression;
	});

	function handleExpressionInput(value: string): void {
		expressionDraft = value;
		clearTimeout(expressionDebounce);
		expressionDebounce = setTimeout(() => explorer.setFormulaExpression(value), 300);
	}
</script>

<div class="flex w-[420px] shrink-0 flex-col gap-3 overflow-y-auto border-r p-4">
	<div class="flex flex-col gap-2">
		{#each explorer.formulaQueries as row (row.letter)}
			<div class="flex flex-wrap items-center gap-2 rounded-md border p-2">
				<Badge variant="secondary" class="w-6 shrink-0 justify-center font-mono">{row.letter}</Badge>

				<PopoverSingleSelect
					label={m.formulaBuilder_metricLabel()}
					triggerLabel={row.metric ? `${row.metric.metricName} · ${row.metric.serviceName}` : m.formulaBuilder_selectMetric()}
					options={metricOptions}
					value={row.metric ? metricKey(row.metric) : ''}
					onChange={(v) => explorer.setFormulaQueryMetric(row.letter, metricByKey.get(v) ?? null)}
				/>

				{#if row.metric}
					{#if row.attributeKeysLoading}
						<Spinner class="size-3.5" />
					{:else if row.attributeKeys.length > 0}
						<PopoverSingleSelect
							label={m.metricsToolbar_groupByLabel()}
							triggerLabel={row.groupByAttributeKey
								? m.metricsToolbar_groupByWithKey({ key: row.groupByAttributeKey })
								: m.metricsToolbar_groupByLabel()}
							options={groupByOptions(row)}
							value={row.groupByAttributeKey ?? GROUP_BY_NONE}
							onChange={(v) => explorer.setFormulaQueryGroupBy(row.letter, v === GROUP_BY_NONE ? null : v)}
						/>
					{/if}
				{/if}

				<Button
					variant="ghost"
					size="icon"
					class="ml-auto size-6"
					disabled={explorer.formulaQueries.length <= 1}
					title={m.formulaBuilder_removeQuery({ letter: row.letter })}
					onclick={() => explorer.removeFormulaQuery(row.letter)}
				>
					<XIcon class="size-3.5" />
				</Button>
			</div>
		{/each}

		<Button
			variant="outline"
			size="sm"
			class="self-start"
			disabled={explorer.formulaQueries.length >= MAX_FORMULA_QUERIES}
			onclick={() => explorer.addFormulaQuery()}
		>
			<PlusIcon data-icon="inline-start" />
			{m.formulaBuilder_addQuery()}
		</Button>
	</div>

	<div class="flex flex-col gap-1">
		<label for="formula-expression" class="text-muted-foreground text-xs font-medium">{m.formulaBuilder_expressionLabel()}</label>
		<Input
			id="formula-expression"
			value={expressionDraft}
			oninput={(e) => handleExpressionInput(e.currentTarget.value)}
			placeholder="(A / B) * 100"
			class="font-mono"
			aria-invalid={explorer.formulaExpressionError != null}
		/>
		{#if explorer.formulaExpressionError}
			<p class="text-destructive text-xs">{explorer.formulaExpressionError}</p>
		{:else}
			<p class="text-muted-foreground text-xs">{m.formulaBuilder_expressionHint()}</p>
		{/if}
	</div>
</div>
