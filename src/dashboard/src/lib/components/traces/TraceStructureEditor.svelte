<script lang="ts">
	// Structural trace query builder: lettered span conditions (service, span name, status,
	// min duration, attribute filters - all optional, ANDed, at least one each) plus an
	// expression over them such as `A -> B AND NOT C`. Edits a local draft; only Apply
	// commits it to TracesExplorerState.filter.structure and re-runs the search, since every
	// run groups the window's spans by trace - too heavy to repeat per keystroke. Apply first
	// asks the server to validate it (TraceStructureSqlBuilder.Validate, no query) and shows
	// the reason inline, so an invalid structure never reaches the list and facet requests. See docs-internal/adr/0069-structural-trace-queries.md.
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import AttributeValueCombobox, { type AttributeValueSuggestion } from '$lib/components/logs/AttributeValueCombobox.svelte';
	import SpanAttributeRowsEditor from './SpanAttributeRowsEditor.svelte';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import { tracesExplorerContext } from '$lib/traces/context';
	import {
		getSpanAttributeValues,
		MAX_STRUCTURE_CONDITIONS,
		validateTraceStructure,
		type SpanAttributeBag,
		type SpanFilter,
		type SpanValuesField,
		type TraceSpanCondition,
		type TraceStructureFilter
	} from '$lib/traces-api';
	import * as m from '$lib/paraglide/messages';

	const explorer = tracesExplorerContext.get();

	const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
	const ANY_STATUS = 'any';

	const STATUS_OPTIONS = [
		{ value: ANY_STATUS, label: m.traceStructure_statusAny() },
		{ value: 'STATUS_CODE_ERROR', label: m.traceStructure_statusError() },
		{ value: 'STATUS_CODE_OK', label: m.traceStructure_statusOk() },
		{ value: 'STATUS_CODE_UNSET', label: m.traceStructure_statusUnset() }
	];

	/** A condition as the editor holds it: attributes always an array, duration in ms as typed. */
	interface DraftCondition {
		name: string;
		serviceName: string;
		spanName: string;
		statusCode: string;
		minDurationMs: string;
		attributes: NonNullable<TraceSpanCondition['attributes']>;
	}

	function draftOf(c: TraceSpanCondition): DraftCondition {
		return {
			name: c.name,
			serviceName: c.serviceName ?? '',
			spanName: c.spanName ?? '',
			statusCode: c.statusCode ?? '',
			minDurationMs: c.minDurationNano == null ? '' : String(c.minDurationNano / 1_000_000),
			attributes: c.attributes ? c.attributes.map((a) => ({ ...a })) : []
		};
	}

	function emptyCondition(name: string): DraftCondition {
		return { name, serviceName: '', spanName: '', statusCode: '', minDurationMs: '', attributes: [] };
	}

	let conditions = $state<DraftCondition[]>([]);
	let expression = $state('');
	let validationError = $state<string | null>(null);
	let applying = $state(false);

	/** Re-seeds the draft from the applied structure - on mount, and whenever it's replaced from outside (saved view, Clear filters). */
	function reset(applied: TraceStructureFilter | null): void {
		conditions = applied ? applied.conditions.map(draftOf) : [emptyCondition('A'), emptyCondition('B')];
		expression = applied?.expression ?? '';
		validationError = null;
	}

	$effect(() => {
		reset(explorer.filter.structure);
	});

	const canAdd = $derived(conditions.length < MAX_STRUCTURE_CONDITIONS);

	function addCondition(): void {
		const used = new Set(conditions.map((c) => c.name));
		const next = [...LETTERS].find((l) => !used.has(l));
		if (next && canAdd) conditions.push(emptyCondition(next));
	}

	function removeCondition(name: string): void {
		conditions = conditions.filter((c) => c.name !== name);
	}

	function toCondition(d: DraftCondition): TraceSpanCondition {
		const ms = Number(d.minDurationMs);
		return {
			name: d.name,
			serviceName: d.serviceName.trim() || undefined,
			spanName: d.spanName.trim() || undefined,
			statusCode: d.statusCode || undefined,
			minDurationNano: d.minDurationMs.trim() !== '' && Number.isFinite(ms) && ms >= 0 ? Math.round(ms * 1_000_000) : undefined,
			attributes: d.attributes.filter((a) => a.key.trim() !== '')
		};
	}

	async function apply(): Promise<void> {
		if (applying) return;
		const structure = { conditions: conditions.map(toCondition), expression: expression.trim() };
		applying = true;
		try {
			validationError = await validateTraceStructure(structure);
		} catch (err) {
			validationError = err instanceof Error ? err.message : String(err);
		} finally {
			applying = false;
		}
		if (validationError == null) explorer.setStructure(structure);
	}

	/** Suggestions from the explorer's current window, so the pickers offer what the query can actually match. */
	async function suggest(
		field: SpanValuesField,
		text: string,
		signal: AbortSignal,
		extra: { filter?: SpanFilter; bag?: SpanAttributeBag; key?: string } = {}
	): Promise<AttributeValueSuggestion[]> {
		try {
			const range = explorer.currentRange();
			const filter: SpanFilter = { ...(range ? { from: range.from, to: range.to } : {}), ...extra.filter };
			const res = await getSpanAttributeValues({ field, key: extra.key ?? '', bag: extra.bag, prefix: text || undefined, filter }, signal);
			return res.values;
		} catch {
			return [];
		}
	}
</script>

<section class="border-b px-4 py-3" aria-label={m.traceStructure_heading()}>
	<p class="text-muted-foreground mb-2 text-xs">{m.traceStructure_intro()}</p>

	<ol class="flex flex-col gap-2">
		{#each conditions as condition (condition.name)}
			<li class="rounded-md border p-2" aria-label={m.traceStructure_conditionHeading({ name: condition.name })}>
				<div class="flex flex-wrap items-center gap-2">
					<span
						class="bg-primary text-primary-foreground flex size-6 shrink-0 items-center justify-center rounded-full font-mono text-xs font-medium"
						title={m.traceStructure_conditionHeading({ name: condition.name })}
					>
						{condition.name}
					</span>
					<AttributeValueCombobox
						class="h-8 w-44 text-sm"
						placeholder={m.traceStructure_servicePlaceholder()}
						value={condition.serviceName}
						oninput={(v) => (condition.serviceName = v)}
						fetchSuggestions={(text, signal) => suggest('Service', text, signal)}
					/>
					<AttributeValueCombobox
						class="h-8 w-52 text-sm"
						placeholder={m.traceStructure_spanNamePlaceholder()}
						value={condition.spanName}
						oninput={(v) => (condition.spanName = v)}
						fetchSuggestions={(text, signal) =>
							suggest('Name', text, signal, { filter: condition.serviceName ? { services: [condition.serviceName] } : {} })}
					/>
					<Select.Root type="single" value={condition.statusCode || ANY_STATUS} onValueChange={(v) => (condition.statusCode = v === ANY_STATUS ? '' : (v ?? ''))}>
						<Select.Trigger class="h-8 w-32 text-sm" aria-label={m.traceStructure_statusLabel()}>
							{STATUS_OPTIONS.find((o) => o.value === (condition.statusCode || ANY_STATUS))?.label}
						</Select.Trigger>
						<Select.Content>
							{#each STATUS_OPTIONS as option (option.value)}
								<Select.Item value={option.value} label={option.label} />
							{/each}
						</Select.Content>
					</Select.Root>
					<Input
						class="h-8 w-32 text-sm"
						type="number"
						min="0"
						placeholder={m.traceStructure_minDurationPlaceholder()}
						aria-label={m.traceStructure_minDurationPlaceholder()}
						value={condition.minDurationMs}
						oninput={(e) => (condition.minDurationMs = e.currentTarget.value)}
					/>
					<Button variant="ghost" size="sm" onclick={() => condition.attributes.push({ bag: 'Span', key: '', value: '', operator: 'Equals' })}>
						<PlusIcon data-icon="inline-start" />
						{m.traceStructure_addAttribute()}
					</Button>
					<Button
						class="ml-auto"
						variant="ghost"
						size="icon-sm"
						onclick={() => removeCondition(condition.name)}
						disabled={conditions.length <= 1}
						aria-label={m.traceStructure_removeCondition({ name: condition.name })}
					>
						<Trash2Icon />
					</Button>
				</div>
				<SpanAttributeRowsEditor
					attributes={condition.attributes}
					onChange={(next) => (condition.attributes = next)}
					fetchValueSuggestions={(bag, key, text, signal) => suggest('Attribute', text, signal, { bag, key })}
				/>
			</li>
		{/each}
	</ol>

	<div class="mt-2 flex flex-wrap items-center gap-2">
		{#if canAdd}
			<Button variant="outline" size="sm" onclick={addCondition}>
				<PlusIcon data-icon="inline-start" />
				{m.traceStructure_addCondition()}
			</Button>
		{/if}
		<Input
			class="h-8 min-w-60 flex-1 font-mono text-sm"
			placeholder={m.traceStructure_expressionPlaceholder()}
			aria-label={m.traceStructure_expressionLabel()}
			value={expression}
			aria-invalid={validationError != null}
			oninput={(e) => (expression = e.currentTarget.value)}
			onkeydown={(e) => e.key === 'Enter' && expression.trim() && void apply()}
		/>
		<Button size="sm" onclick={() => void apply()} disabled={!expression.trim() || applying}>{m.traceStructure_apply()}</Button>
		{#if explorer.filter.structure}
			<Button variant="ghost" size="sm" onclick={() => explorer.setStructure(null)}>{m.traceStructure_remove()}</Button>
		{/if}
	</div>
	{#if validationError}
		<p class="text-destructive mt-1.5 text-xs" role="alert">{validationError}</p>
	{/if}
	<p class="text-muted-foreground mt-1.5 text-xs">{m.traceStructure_syntaxHint()}</p>
</section>
