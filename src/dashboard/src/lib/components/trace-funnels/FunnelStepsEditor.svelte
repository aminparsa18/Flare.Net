<script lang="ts">
	// The funnel's ordered steps, one card each: service, span name and attribute filters
	// (all optional, ANDed; at least one needed - see TraceFunnelQueryBuilder.ValidateSteps).
	// Edits only change TraceFunnelState.steps; nothing runs until the Run button (see that
	// class's header for why). The attribute rows mirror SpanAttributeFiltersRow.svelte's
	// controls, without its debounced auto-commit.
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import AttributeValueCombobox, { type AttributeValueSuggestion } from '$lib/components/logs/AttributeValueCombobox.svelte';
	import AttributeValueListInput from '$lib/components/logs/AttributeValueListInput.svelte';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import { traceFunnelContext } from '$lib/trace-funnels/context';
	import type { EditableStep } from '$lib/trace-funnels/state.svelte';
	import { isStepDefined } from '$lib/trace-funnels-api';
	import {
		getSpanAttributeValues,
		type SpanAttributeBag,
		type SpanAttributeFilter,
		type SpanAttributeFilterOperator,
		type SpanFilter,
		type SpanValuesField
	} from '$lib/traces-api';
	import * as m from '$lib/paraglide/messages';

	const funnel = traceFunnelContext.get();

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
		{ value: 'NotIn', label: m.attributeFilters_opNotIn() }
	];

	function needsSingleValue(operator: SpanAttributeFilterOperator | undefined): boolean {
		return operator == null || operator === 'Equals' || operator === 'NotEquals' || operator === 'Regex' || operator === 'NotRegex';
	}

	function needsMultiValue(operator: SpanAttributeFilterOperator | undefined): boolean {
		return operator === 'In' || operator === 'NotIn';
	}

	/** Suggestions from the funnel's own window, so the pickers offer what the funnel can actually match. */
	async function suggest(
		field: SpanValuesField,
		text: string,
		signal: AbortSignal,
		extra: { filter?: SpanFilter; bag?: SpanAttributeBag; key?: string } = {}
	): Promise<AttributeValueSuggestion[]> {
		try {
			const filter: SpanFilter = { from: new Date(Date.now() - funnel.minutes() * 60_000).toISOString(), ...extra.filter };
			const res = await getSpanAttributeValues({ field, key: extra.key ?? '', bag: extra.bag, prefix: text || undefined, filter }, signal);
			return res.values;
		} catch {
			return [];
		}
	}

	function updateAttribute(step: EditableStep, index: number, patch: Partial<SpanAttributeFilter>): void {
		funnel.updateStep(step.id, { attributes: step.attributes.map((a, i) => (i === index ? { ...a, ...patch } : a)) });
	}

	function addAttribute(step: EditableStep): void {
		funnel.updateStep(step.id, { attributes: [...step.attributes, { bag: 'Span', key: '', value: '', operator: 'Equals' }] });
	}

	function removeAttribute(step: EditableStep, index: number): void {
		funnel.updateStep(step.id, { attributes: step.attributes.filter((_, i) => i !== index) });
	}
</script>

<ol class="flex flex-col gap-3 px-4 py-3">
	{#each funnel.steps as step, index (step.id)}
		<li class="rounded-lg border p-3" aria-label={m.funnelsPage_stepHeading({ number: index + 1 })}>
			<div class="mb-2 flex items-center gap-2">
				<span class="bg-primary text-primary-foreground flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-medium tabular-nums">
					{index + 1}
				</span>
				<span class="text-sm font-medium">{m.funnelsPage_stepHeading({ number: index + 1 })}</span>
				{#if !isStepDefined(step)}
					<span class="text-muted-foreground text-xs">{m.funnelsPage_stepEmptyHint()}</span>
				{/if}
				<div class="ml-auto flex items-center gap-0.5">
					<Button variant="ghost" size="icon-sm" onclick={() => funnel.moveStep(step.id, -1)} disabled={index === 0} aria-label={m.funnelsPage_moveUp()}>
						<ArrowUpIcon />
					</Button>
					<Button
						variant="ghost"
						size="icon-sm"
						onclick={() => funnel.moveStep(step.id, 1)}
						disabled={index === funnel.steps.length - 1}
						aria-label={m.funnelsPage_moveDown()}
					>
						<ArrowDownIcon />
					</Button>
					<Button variant="ghost" size="icon-sm" onclick={() => funnel.removeStep(step.id)} disabled={!funnel.canRemoveStep} aria-label={m.funnelsPage_removeStep()}>
						<Trash2Icon />
					</Button>
				</div>
			</div>

			<div class="flex flex-wrap items-center gap-2">
				<AttributeValueCombobox
					class="h-8 w-52 text-sm"
					placeholder={m.funnelsPage_servicePlaceholder()}
					value={step.serviceName}
					oninput={(v) => funnel.updateStep(step.id, { serviceName: v })}
					fetchSuggestions={(text, signal) => suggest('Service', text, signal)}
				/>
				<AttributeValueCombobox
					class="h-8 w-64 text-sm"
					placeholder={m.funnelsPage_spanNamePlaceholder()}
					value={step.spanName}
					oninput={(v) => funnel.updateStep(step.id, { spanName: v })}
					fetchSuggestions={(text, signal) =>
						suggest('Name', text, signal, { filter: step.serviceName ? { services: [step.serviceName] } : {} })}
				/>
				<Button variant="ghost" size="sm" onclick={() => addAttribute(step)}>
					<PlusIcon data-icon="inline-start" />
					{m.funnelsPage_addAttribute()}
				</Button>
			</div>

			{#if step.attributes.length > 0}
				<div class="mt-2 flex flex-col gap-1.5">
					{#each step.attributes as attribute, attrIndex (attrIndex)}
						<div class="flex flex-wrap items-center gap-1.5">
							<Select.Root type="single" value={attribute.bag} onValueChange={(v) => v && updateAttribute(step, attrIndex, { bag: v as SpanAttributeBag })}>
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
								oninput={(e) => updateAttribute(step, attrIndex, { key: e.currentTarget.value })}
							/>
							<Select.Root
								type="single"
								value={attribute.operator ?? 'Equals'}
								onValueChange={(v) => v && updateAttribute(step, attrIndex, { operator: v as SpanAttributeFilterOperator })}
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
									oninput={(v) => updateAttribute(step, attrIndex, { value: v })}
									fetchSuggestions={(text, signal) => suggest('Attribute', text, signal, { bag: attribute.bag, key: attribute.key })}
								/>
							{:else if needsMultiValue(attribute.operator)}
								<AttributeValueListInput
									values={attribute.values ?? []}
									onChange={(next) => updateAttribute(step, attrIndex, { values: next })}
									fetchSuggestions={(text, signal) => suggest('Attribute', text, signal, { bag: attribute.bag, key: attribute.key })}
								/>
							{/if}
							<button
								type="button"
								class="text-muted-foreground hover:text-foreground"
								onclick={() => removeAttribute(step, attrIndex)}
								aria-label={m.attributeFilters_removeFilter()}
							>
								<XIcon class="size-3.5" />
							</button>
						</div>
					{/each}
				</div>
			{/if}
		</li>
	{/each}
</ol>

{#if funnel.canAddStep}
	<div class="px-4">
		<Button variant="outline" size="sm" onclick={() => funnel.addStep()}>
			<PlusIcon data-icon="inline-start" />
			{m.funnelsPage_addStep()}
		</Button>
	</div>
{/if}
