<script lang="ts">
	// Structured builder for BodyJsonFilter's exists/absent/equals/not-equals/regex/
	// not-regex/in/not-in/has/not-has operators (see LogFilterSqlBuilder.cs's BodyJsonClause) - same
	// discrete-controls shape AttributeFiltersRow.svelte already establishes for
	// AttributeFilter, minus the bag selector (BodyJsonFilter always targets Body) and
	// plus a free-text "path" field (dot-separated object keys, e.g. "user.id") instead of
	// an attribute key. Own Accordion section, same collapsed-by-default/
	// localStorage-remembered pattern AttributeFiltersRow/SqlQueryRow already use.
	//
	// Unlike AttributeFiltersRow, there's no server-side value-suggestion endpoint for a
	// JSON path (getLogAttributeValues is keyed to a bag+key pair, not a Body path) - the
	// value/values inputs still reuse AttributeValueCombobox/AttributeValueListInput for
	// their input-and-chip behavior (Enter/comma to commit, etc.), just wired to a
	// suggestion source that always resolves empty rather than a real lookup.
	//
	// Rows are local, ephemeral UI state (each needs a stable key for {#each} that
	// BodyJsonFilter itself has no field for) - committed into
	// explorer.filter.bodyJsonFilters (and re-run) via explorer.setBodyJsonFilters
	// whenever a row's committed shape actually changes. Operator dropdown and add/remove
	// commit immediately; path/value text inputs debounce (300ms), same reasoning
	// AttributeFiltersRow's own header gives.
	import { browser } from '$app/environment';
	import { logsExplorerContext } from '$lib/logs/context';
	import type { BodyJsonFilter, BodyJsonFilterOperator } from '$lib/api';
	import * as Accordion from '$lib/components/ui/accordion';
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import AttributeValueCombobox from './AttributeValueCombobox.svelte';
	import AttributeValueListInput from './AttributeValueListInput.svelte';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.get();

	const ITEM = 'body-json-filters';
	const COLLAPSE_STORAGE_KEY = 'flare.logs.bodyJsonFiltersCollapsed';

	function loadStoredValue(): string {
		if (!browser) return ''; // collapsed by default - see this file's own header remarks
		try {
			return localStorage.getItem(COLLAPSE_STORAGE_KEY) === 'false' ? ITEM : '';
		} catch {
			return ''; // storage disabled (e.g. private browsing) - fall back to collapsed
		}
	}

	let accordionValue = $state(loadStoredValue());

	$effect(() => {
		const value = accordionValue;
		if (!browser) return;
		try {
			localStorage.setItem(COLLAPSE_STORAGE_KEY, String(value !== ITEM));
		} catch {
			// Non-critical - the next reload just falls back to collapsed instead.
		}
	});

	// Operator labels are reused verbatim from AttributeFiltersRow's own i18n keys - same
	// text, same meaning (BodyJsonFilterOperator's first eight members mirror
	// AttributeFilterOperator's exactly), not worth duplicating across three locale files.
	// Only the array-only Has/NotHas need their own keys.
	const OPERATOR_OPTIONS: { value: BodyJsonFilterOperator; label: string }[] = [
		{ value: 'Equals', label: m.attributeFilters_opEquals() },
		{ value: 'NotEquals', label: m.attributeFilters_opNotEquals() },
		{ value: 'Exists', label: m.attributeFilters_opExists() },
		{ value: 'Absent', label: m.attributeFilters_opAbsent() },
		{ value: 'Regex', label: m.attributeFilters_opRegex() },
		{ value: 'NotRegex', label: m.attributeFilters_opNotRegex() },
		{ value: 'In', label: m.attributeFilters_opIn() },
		{ value: 'NotIn', label: m.attributeFilters_opNotIn() },
		{ value: 'Has', label: m.bodyJsonFilters_opHas() },
		{ value: 'NotHas', label: m.bodyJsonFilters_opNotHas() }
	];

	/** Exists/Absent ignore BodyJsonFilter.value entirely - see BodyJsonFilterOperator's own remarks (LogFilter.cs); In/NotIn ignore it too, taking their operand from `values` instead (see needsMultiValue). Has/NotHas use `value` as the array element to look for. */
	function needsSingleValue(operator: BodyJsonFilterOperator): boolean {
		return (
			operator === 'Equals' ||
			operator === 'NotEquals' ||
			operator === 'Regex' ||
			operator === 'NotRegex' ||
			operator === 'Has' ||
			operator === 'NotHas'
		);
	}

	/** In/NotIn's multi-value operand - AttributeValueListInput's chip editor, rather than a single AttributeValueCombobox. */
	function needsMultiValue(operator: BodyJsonFilterOperator): boolean {
		return operator === 'In' || operator === 'NotIn';
	}

	/** No suggestion source exists for a JSON path's value (unlike AttributeFiltersRow's getLogAttributeValues) - AttributeValueCombobox/AttributeValueListInput still need a `fetchSuggestions` prop, so this always resolves empty rather than a real lookup. */
	async function noSuggestions() {
		return [];
	}

	interface Row {
		id: number;
		path: string;
		operator: BodyJsonFilterOperator;
		value: string;
		values: string[];
	}

	let nextId = 0;

	function toRows(filters: BodyJsonFilter[]): Row[] {
		return filters.map((f) => ({ id: nextId++, path: f.path, operator: f.operator ?? 'Equals', value: f.value, values: f.values ?? [] }));
	}

	let rows = $state<Row[]>(toRows(explorer.filter.bodyJsonFilters));

	/** Cheap content snapshot for the resync guard below - order-sensitive is fine, since commit() always writes rows in their current on-screen order. */
	function snapshotOf(filters: BodyJsonFilter[]): string {
		return JSON.stringify(filters.map((f) => [f.path, f.operator ?? 'Equals', f.value, f.values ?? []]));
	}

	// Same "tell my own commit() apart from an outside filter change" guard
	// AttributeFiltersRow's own remarks explain in full - without it, commit()'s own write
	// bounces back through this effect and re-derives `rows` from the *committed* filters,
	// dropping any row with an empty/mid-edit path.
	let lastAppliedSnapshot = snapshotOf(explorer.filter.bodyJsonFilters);

	$effect(() => {
		const snapshot = snapshotOf(explorer.filter.bodyJsonFilters);
		if (snapshot === lastAppliedSnapshot) return;
		lastAppliedSnapshot = snapshot;
		rows = toRows(explorer.filter.bodyJsonFilters);
	});

	/** Only rows with a non-empty path are sent - a row mid-edit (path not typed yet) shouldn't turn into a nonsensical `""` path filter. */
	function commit(): void {
		const filters: BodyJsonFilter[] = rows
			.filter((r) => r.path.trim())
			.map((r) => ({
				path: r.path.trim(),
				operator: r.operator,
				value: needsSingleValue(r.operator) ? r.value : '',
				values: needsMultiValue(r.operator) ? r.values : undefined
			}));
		lastAppliedSnapshot = snapshotOf(filters);
		explorer.setBodyJsonFilters(filters);
	}

	let commitDebounce: ReturnType<typeof setTimeout> | undefined;
	function commitDebounced(): void {
		clearTimeout(commitDebounce);
		commitDebounce = setTimeout(commit, 300);
	}

	function addRow(): void {
		rows = [...rows, { id: nextId++, path: '', operator: 'Equals', value: '', values: [] }];
	}

	function removeRow(id: number): void {
		rows = rows.filter((r) => r.id !== id);
		clearTimeout(commitDebounce);
		commit();
	}

	function updateRow(id: number, patch: Partial<Row>): void {
		rows = rows.map((r) => (r.id === id ? { ...r, ...patch } : r));
	}

	const activeCount = $derived(explorer.filter.bodyJsonFilters.length);
</script>

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-b">
	<Accordion.Item value={ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				{m.bodyJsonFilters_label()}
			</Accordion.Trigger>
			{#if activeCount > 0}
				<span class="text-muted-foreground tabular-nums">{m.bodyJsonFilters_activeCount({ count: activeCount })}</span>
			{/if}
		</div>
		<Accordion.Content class="px-4 pb-3">
			<div class="flex flex-col gap-2">
				{#each rows as row (row.id)}
					<div class="flex flex-wrap items-center gap-1.5">
						<Input
							class="h-7 w-48 text-xs"
							placeholder={m.bodyJsonFilters_pathPlaceholder()}
							value={row.path}
							oninput={(e) => {
								updateRow(row.id, { path: e.currentTarget.value });
								commitDebounced();
							}}
						/>

						<Select.Root
							type="single"
							value={row.operator}
							onValueChange={(v) => {
								if (!v) return;
								updateRow(row.id, { operator: v as BodyJsonFilterOperator });
								commit();
							}}
						>
							<Select.Trigger class="h-7 w-40 text-xs">
								{OPERATOR_OPTIONS.find((o) => o.value === row.operator)?.label}
							</Select.Trigger>
							<Select.Content>
								{#each OPERATOR_OPTIONS as option (option.value)}
									<Select.Item value={option.value} label={option.label} />
								{/each}
							</Select.Content>
						</Select.Root>

						{#if needsSingleValue(row.operator)}
							<AttributeValueCombobox
								class="h-7 w-40 text-xs"
								placeholder={m.attributeFilters_valuePlaceholder()}
								value={row.value}
								oninput={(v) => {
									updateRow(row.id, { value: v });
									commitDebounced();
								}}
								fetchSuggestions={noSuggestions}
							/>
						{:else if needsMultiValue(row.operator)}
							<AttributeValueListInput
								values={row.values}
								onChange={(next) => {
									updateRow(row.id, { values: next });
									commit();
								}}
								fetchSuggestions={noSuggestions}
							/>
						{/if}

						<button
							type="button"
							class="text-muted-foreground hover:text-foreground"
							onclick={() => removeRow(row.id)}
							aria-label={m.bodyJsonFilters_removeFilter()}
						>
							<XIcon class="size-3.5" />
						</button>
					</div>
				{/each}

				<Button variant="outline" size="sm" class="w-fit" onclick={addRow}>
					<PlusIcon data-icon="inline-start" />
					{m.bodyJsonFilters_addFilter()}
				</Button>
			</div>
		</Accordion.Content>
	</Accordion.Item>
</Accordion.Root>
