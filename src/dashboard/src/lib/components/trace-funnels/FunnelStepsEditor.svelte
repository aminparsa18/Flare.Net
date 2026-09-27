<script lang="ts">
	// The funnel's ordered steps, one card each: service, span name and attribute filters
	// (all optional, ANDed; at least one needed - see TraceFunnelQueryBuilder.ValidateSteps).
	// Edits only change TraceFunnelState.steps; nothing runs until the Run button (see that
	// class's header for why).
	import { Button } from '$lib/components/ui/button';
	import AttributeValueCombobox, { type AttributeValueSuggestion } from '$lib/components/logs/AttributeValueCombobox.svelte';
	import SpanAttributeRowsEditor from '$lib/components/traces/SpanAttributeRowsEditor.svelte';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import { traceFunnelContext } from '$lib/trace-funnels/context';
	import type { EditableStep } from '$lib/trace-funnels/state.svelte';
	import { isStepDefined } from '$lib/trace-funnels-api';
	import { getSpanAttributeValues, type SpanAttributeBag, type SpanFilter, type SpanValuesField } from '$lib/traces-api';
	import * as m from '$lib/paraglide/messages';

	const funnel = traceFunnelContext.get();

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

	function addAttribute(step: EditableStep): void {
		funnel.updateStep(step.id, { attributes: [...step.attributes, { bag: 'Span', key: '', value: '', operator: 'Equals' }] });
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

			<SpanAttributeRowsEditor
				attributes={step.attributes}
				onChange={(next) => funnel.updateStep(step.id, { attributes: next })}
				fetchValueSuggestions={(bag, key, text, signal) => suggest('Attribute', text, signal, { bag, key })}
			/>
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
