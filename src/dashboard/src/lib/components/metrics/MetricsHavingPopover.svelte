<script lang="ts">
	// Post-aggregation value filter (roadmap's "Post-aggregation value filter (HAVING) +
	// top-N order-by on metric queries" item) - "only series where the aggregated value
	// exceeds X", compiled to a real ClickHouse HAVING clause by MetricSeriesQueryBuilder
	// (see its own remarks) rather than an app-side filter over already-fetched rows. Same
	// "small icon-triggered popover with a mini form" shape as YAxisBoundsPopover.svelte/
	// ApdexThresholdPopover.svelte, the closest existing precedents for this kind of
	// affordance in this codebase.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import FilterIcon from '@lucide/svelte/icons/filter';
	import * as m from '$lib/paraglide/messages';
	import type { MetricHavingOperator } from '$lib/metrics-api';

	let {
		havingOperator,
		havingValue,
		onApply
	}: {
		havingOperator: MetricHavingOperator | null;
		havingValue: number | null;
		onApply: (operator: MetricHavingOperator | null, value: number | null) => void;
	} = $props();

	// PopoverSingleSelect-style sentinel for "no filter" - Bits UI's Select needs a
	// non-empty string value, same trick MetricsToolbar's own GROUP_BY_NONE uses.
	const HAVING_NONE = '__none__';

	const OPERATOR_OPTIONS: { value: MetricHavingOperator; label: string }[] = [
		{ value: 'GreaterThan', label: '>' },
		{ value: 'GreaterThanOrEqual', label: '≥' },
		{ value: 'LessThan', label: '<' },
		{ value: 'LessThanOrEqual', label: '≤' },
		{ value: 'Equal', label: '=' },
		{ value: 'NotEqual', label: '≠' }
	];

	let open = $state(false);
	// Re-seeded from the latest applied values each time this is opened - see
	// YAxisBoundsPopover.svelte's identical `$effect` for why (referencing the props
	// directly here would only capture their initial value, not stay reactive to later
	// changes - svelte's `state_referenced_locally`).
	let operatorDraft = $state(HAVING_NONE);
	// string when empty/typed, but Svelte's native `bind:value` on `<input type="number">`
	// (inside Input.svelte) coerces this to a real `number` the moment a valid numeric value
	// lands, even though this starts life as `''` - so `.trim()` below always goes through
	// `String(...)` first rather than assuming it stayed a string.
	let valueDraft = $state<string | number>('');
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			operatorDraft = havingOperator ?? HAVING_NONE;
			valueDraft = havingValue != null ? String(havingValue) : '';
			error = null;
		}
	});

	const hasOverride = $derived(havingOperator != null);
	const operatorLabel = $derived(OPERATOR_OPTIONS.find((o) => o.value === operatorDraft)?.label ?? m.metricsHaving_operatorPlaceholder());

	function apply(): void {
		if (operatorDraft === HAVING_NONE) {
			onApply(null, null);
			open = false;
			return;
		}
		const valueText = String(valueDraft);
		const parsed = Number(valueText);
		if (valueText.trim() === '' || !Number.isFinite(parsed)) {
			error = m.metricsHaving_invalidValue();
			return;
		}
		onApply(operatorDraft as MetricHavingOperator, parsed);
		open = false;
	}

	function clear(): void {
		onApply(null, null);
		open = false;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="outline"
				size="sm"
				class={hasOverride ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}
				title={m.metricsHaving_title()}
			>
				<FilterIcon class="size-3.5" data-icon="inline-start" />
				{hasOverride ? m.metricsHaving_activeLabel({ op: operatorLabel, value: havingValue ?? 0 }) : m.metricsHaving_label()}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64" align="start">
		<p class="mb-1 text-sm font-medium">{m.metricsHaving_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.metricsHaving_description()}</p>
		<div class="flex items-center gap-2">
			<Select.Root type="single" value={operatorDraft} onValueChange={(v) => v && (operatorDraft = v)}>
				<Select.Trigger class="h-8 w-20">
					{operatorDraft === HAVING_NONE ? m.metricsHaving_operatorPlaceholder() : operatorLabel}
				</Select.Trigger>
				<Select.Content>
					<Select.Item value={HAVING_NONE} label={m.metricsHaving_operatorPlaceholder()} />
					{#each OPERATOR_OPTIONS as option (option.value)}
						<Select.Item value={option.value} label={option.label} />
					{/each}
				</Select.Content>
			</Select.Root>
			<Input
				type="number"
				bind:value={valueDraft}
				disabled={operatorDraft === HAVING_NONE}
				class="h-8"
				placeholder={m.metricsHaving_valuePlaceholder()}
			/>
		</div>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasOverride}>{m.metricsHaving_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.metricsHaving_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
