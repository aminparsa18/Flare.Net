<script lang="ts">
	// Per-query post-processing functions (roadmap: "Per-query post-processing functions
	// (metrics and logs)", ADR-0038) - a chainable list of app-side transforms
	// (clamp-min/max, absolute, log2/log10, cumulative-sum) applied in order to the
	// queried series' points server-side (MetricPostProcessor.cs), same "small
	// icon-triggered popover with a mini form" shape as MetricsHavingPopover.svelte, just
	// a list of rows instead of one operator/value pair.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import SquareFunctionIcon from '@lucide/svelte/icons/square-function';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';
	import type { MetricPostProcessFunction, MetricPostProcessFunctionType } from '$lib/metrics-api';

	let {
		functions,
		onApply
	}: {
		functions: MetricPostProcessFunction[];
		onApply: (functions: MetricPostProcessFunction[]) => void;
	} = $props();

	// Same "closed, sane set of choices, capped" call FormulaBuilder.svelte's
	// MAX_FORMULA_QUERIES makes - a chain this long has no real use case and keeps the
	// popover from growing unboundedly.
	const MAX_FUNCTIONS = 5;

	const FUNCTION_OPTIONS: { value: MetricPostProcessFunctionType; label: () => string }[] = [
		{ value: 'ClampMin', label: m.metricsFunctions_clampMin },
		{ value: 'ClampMax', label: m.metricsFunctions_clampMax },
		{ value: 'Absolute', label: m.metricsFunctions_absolute },
		{ value: 'Log2', label: m.metricsFunctions_log2 },
		{ value: 'Log10', label: m.metricsFunctions_log10 },
		{ value: 'CumulativeSum', label: m.metricsFunctions_cumulativeSum }
	];

	function needsValue(type: MetricPostProcessFunctionType): boolean {
		return type === 'ClampMin' || type === 'ClampMax';
	}

	let open = $state(false);
	// Re-seeded from the latest applied value each time this is opened - same reactive
	// re-seed MetricsHavingPopover.svelte's own `$effect` uses, for the same
	// `state_referenced_locally` reason (referencing the prop directly would only
	// capture its initial value).
	// `value` starts life as a string but Svelte's native `bind:value` on
	// `<input type="number">` (inside Input.svelte) coerces it to a real `number` the
	// moment a valid numeric value lands - same gotcha MetricsHavingPopover.svelte's own
	// `valueDraft` documents, so `apply()` below always goes through `String(...)` first
	// rather than assuming it stayed a string.
	let draft = $state<{ type: MetricPostProcessFunctionType; value: string | number }[]>([]);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			draft = functions.map((f) => ({ type: f.type, value: f.value != null ? String(f.value) : '' }));
			error = null;
		}
	});

	const hasOverride = $derived(functions.length > 0);

	function addRow(): void {
		if (draft.length >= MAX_FUNCTIONS) return;
		draft = [...draft, { type: 'Absolute', value: '' }];
	}

	function removeRow(index: number): void {
		draft = draft.filter((_, i) => i !== index);
	}

	function apply(): void {
		const resolved: MetricPostProcessFunction[] = [];
		for (const row of draft) {
			if (!needsValue(row.type)) {
				resolved.push({ type: row.type });
				continue;
			}
			const valueText = String(row.value);
			const parsed = Number(valueText);
			if (valueText.trim() === '' || !Number.isFinite(parsed)) {
				error = m.metricsFunctions_invalidValue();
				return;
			}
			resolved.push({ type: row.type, value: parsed });
		}
		onApply(resolved);
		open = false;
	}

	function clear(): void {
		onApply([]);
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
				title={m.metricsFunctions_title()}
			>
				<SquareFunctionIcon class="size-3.5" data-icon="inline-start" />
				{hasOverride ? m.metricsFunctions_activeLabel({ count: functions.length }) : m.metricsFunctions_label()}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-80" align="start">
		<p class="mb-1 text-sm font-medium">{m.metricsFunctions_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.metricsFunctions_description()}</p>
		<div class="flex flex-col gap-2">
			{#each draft as row, index (index)}
				<div class="flex items-center gap-2">
					<Select.Root
						type="single"
						value={row.type}
						onValueChange={(v) => v && (draft[index] = { ...draft[index], type: v as MetricPostProcessFunctionType })}
					>
						<Select.Trigger class="h-8 flex-1">
							{FUNCTION_OPTIONS.find((o) => o.value === row.type)?.label()}
						</Select.Trigger>
						<Select.Content>
							{#each FUNCTION_OPTIONS as option (option.value)}
								<Select.Item value={option.value} label={option.label()} />
							{/each}
						</Select.Content>
					</Select.Root>
					{#if needsValue(row.type)}
						<Input
							type="number"
							bind:value={row.value}
							class="h-8 w-20"
							placeholder={m.metricsHaving_valuePlaceholder()}
						/>
					{/if}
					<Button variant="ghost" size="icon" class="size-8 shrink-0" onclick={() => removeRow(index)} title={m.metricsFunctions_remove()}>
						<XIcon class="size-3.5" />
					</Button>
				</div>
			{/each}
		</div>
		<Button variant="outline" size="sm" class="mt-2 w-full" onclick={addRow} disabled={draft.length >= MAX_FUNCTIONS}>
			<PlusIcon class="size-3.5" data-icon="inline-start" />
			{m.metricsFunctions_addFunction()}
		</Button>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasOverride && draft.length === 0}>{m.metricsFunctions_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.metricsFunctions_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
