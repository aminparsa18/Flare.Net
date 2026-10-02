<script lang="ts">
	// Editable list of span attribute filters (bag, key, operator, value/values) with no
	// auto-commit - every edit calls `onChange` with the full next array and the owner
	// decides when to run. Shared by the funnel step cards and the trace-structure condition
	// cards; the Traces explorer's own filter row (SpanAttributeFiltersRow.svelte) has
	// debounced commits and chips instead, so it doesn't use this.
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import AttributeValueCombobox, { type AttributeValueSuggestion } from '$lib/components/logs/AttributeValueCombobox.svelte';
	import AttributeValueListInput from '$lib/components/logs/AttributeValueListInput.svelte';
	import XIcon from '@lucide/svelte/icons/x';
	import type { SpanAttributeBag, SpanAttributeFilter, SpanAttributeFilterOperator } from '$lib/traces-api';
	import * as m from '$lib/paraglide/messages';

	interface Props {
		attributes: SpanAttributeFilter[];
		onChange: (next: SpanAttributeFilter[]) => void;
		/** Value suggestions for one attribute key. */
		fetchValueSuggestions: (bag: SpanAttributeBag, key: string, text: string, signal: AbortSignal) => Promise<AttributeValueSuggestion[]>;
	}

	let { attributes, onChange, fetchValueSuggestions }: Props = $props();

	const BAG_OPTIONS: { value: SpanAttributeBag; label: string }[] = [
		{ value: 'Span', label: m.attributeFilters_bagSpan() },
		{ value: 'Resource', label: m.attributeFilters_bagResource() },
		{ value: 'Scope', label: m.attributeFilters_bagScope() }
	];

	const OPERATOR_OPTIONS: { value: SpanAttributeFilterOperator; label: string }[] = [
		{ value: 'Equals', label: m.attributeFilters_opEquals() },
		{ value: 'NotEquals', label: m.attributeFilters_opNotEquals() },
		{ value: 'Exists', label: m.attributeFilters_opExists() },
		{ value: 'Absent', label: m.attributeFilters_opAbsent() },
		{ value: 'Regex', label: m.attributeFilters_opRegex() },
		{ value: 'NotRegex', label: m.attributeFilters_opNotRegex() },
		{ value: 'In', label: m.attributeFilters_opIn() },
		{ value: 'NotIn', label: m.attributeFilters_opNotIn() },
		{ value: 'GreaterThan', label: m.attributeFilters_opGreaterThan() },
		{ value: 'GreaterThanOrEqual', label: m.attributeFilters_opGreaterThanOrEqual() },
		{ value: 'LessThan', label: m.attributeFilters_opLessThan() },
		{ value: 'LessThanOrEqual', label: m.attributeFilters_opLessThanOrEqual() }
	];

	function needsSingleValue(operator: SpanAttributeFilterOperator | undefined): boolean {
		return operator == null || operator === 'Equals' || operator === 'NotEquals' || operator === 'Regex' || operator === 'NotRegex' || isNumericOperator(operator);
	}

	/** GreaterThan/GreaterThanOrEqual/LessThan/LessThanOrEqual - the value is parsed as a number server-side. */
	function isNumericOperator(operator: SpanAttributeFilterOperator | undefined): boolean {
		return operator === 'GreaterThan' || operator === 'GreaterThanOrEqual' || operator === 'LessThan' || operator === 'LessThanOrEqual';
	}

	function needsMultiValue(operator: SpanAttributeFilterOperator | undefined): boolean {
		return operator === 'In' || operator === 'NotIn';
	}

	function update(index: number, patch: Partial<SpanAttributeFilter>): void {
		onChange(attributes.map((a, i) => (i === index ? { ...a, ...patch } : a)));
	}

	function remove(index: number): void {
		onChange(attributes.filter((_, i) => i !== index));
	}
</script>

{#if attributes.length > 0}
	<div class="mt-2 flex flex-col gap-1.5">
		{#each attributes as attribute, index (index)}
			<div class="flex flex-wrap items-center gap-1.5">
				<Select.Root type="single" value={attribute.bag} onValueChange={(v) => v && update(index, { bag: v as SpanAttributeBag })}>
					<Select.Trigger class="h-7 w-24 text-xs">
						{BAG_OPTIONS.find((o) => o.value === attribute.bag)?.label}
					</Select.Trigger>
					<Select.Content>
						{#each BAG_OPTIONS as option (option.value)}
							<Select.Item value={option.value} label={option.label} />
						{/each}
					</Select.Content>
				</Select.Root>
				<Input
					class="h-7 w-40 text-xs"
					placeholder={m.attributeFilters_keyPlaceholder()}
					value={attribute.key}
					oninput={(e) => update(index, { key: e.currentTarget.value })}
				/>
				<Select.Root
					type="single"
					value={attribute.operator ?? 'Equals'}
					onValueChange={(v) => v && update(index, { operator: v as SpanAttributeFilterOperator })}
				>
					<Select.Trigger class="h-7 w-32 text-xs">
						{OPERATOR_OPTIONS.find((o) => o.value === (attribute.operator ?? 'Equals'))?.label}
					</Select.Trigger>
					<Select.Content>
						{#each OPERATOR_OPTIONS as option (option.value)}
							<Select.Item value={option.value} label={option.label} />
						{/each}
					</Select.Content>
				</Select.Root>
				{#if needsSingleValue(attribute.operator)}
					<AttributeValueCombobox
						class="h-7 w-40 text-xs"
						placeholder={m.attributeFilters_valuePlaceholder()}
						value={attribute.value}
						oninput={(v) => update(index, { value: v })}
						fetchSuggestions={(text, signal) => fetchValueSuggestions(attribute.bag, attribute.key, text, signal)}
					/>
				{:else if needsMultiValue(attribute.operator)}
					<AttributeValueListInput
						values={attribute.values ?? []}
						onChange={(next) => update(index, { values: next })}
						fetchSuggestions={(text, signal) => fetchValueSuggestions(attribute.bag, attribute.key, text, signal)}
					/>
				{/if}
				<button type="button" class="text-muted-foreground hover:text-foreground" onclick={() => remove(index)} aria-label={m.attributeFilters_removeFilter()}>
					<XIcon class="size-3.5" />
				</button>
			</div>
		{/each}
	</div>
{/if}
