<script lang="ts">
	// Structured builder for AttributeFilter's exists/absent/equals/not-equals operators
	// (see LogFilterSqlBuilder.cs's AttributeClause) - discrete controls rather than typed
	// syntax, since LogQL itself has no attribute-map syntax to extend yet (see
	// roadmap.md's "LogQL attribute-map syntax" item). Own Accordion section, same
	// collapsed-by-default/localStorage-remembered pattern SqlQueryRow/
	// ValueDistributionChart already use - a power-user extra, shouldn't claim vertical
	// space above the log table until opened once.
	//
	// Rows are local, ephemeral UI state (each needs a stable key for {#each} that
	// AttributeFilter itself has no field for) - committed into
	// explorer.filter.attributeFilters (and re-run) via explorer.setAttributeFilters
	// whenever a row's committed shape actually changes. Bag/operator dropdowns and
	// add/remove commit immediately; key/value text inputs debounce (300ms), same
	// "typing shouldn't fire a request per keystroke" reasoning LogsToolbar's search box
	// already documents.
	import { browser } from '$app/environment';
	import { logsExplorerContext } from '$lib/logs/context';
	import { getLogAttributeValues, type AttributeBag, type AttributeFilter, type AttributeFilterOperator } from '$lib/api';
	import * as Accordion from '$lib/components/ui/accordion';
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import AttributeValueCombobox, { type AttributeValueSuggestion } from './AttributeValueCombobox.svelte';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.get();

	const ITEM = 'attribute-filters';
	const COLLAPSE_STORAGE_KEY = 'flare.logs.attributeFiltersCollapsed';

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

	const BAG_OPTIONS: { value: AttributeBag; label: string }[] = [
		{ value: 'Log', label: m.attributeFilters_bagLog() },
		{ value: 'Resource', label: m.attributeFilters_bagResource() },
		{ value: 'Scope', label: m.attributeFilters_bagScope() }
	];

	const OPERATOR_OPTIONS: { value: AttributeFilterOperator; label: string }[] = [
		{ value: 'Equals', label: m.attributeFilters_opEquals() },
		{ value: 'NotEquals', label: m.attributeFilters_opNotEquals() },
		{ value: 'Exists', label: m.attributeFilters_opExists() },
		{ value: 'Absent', label: m.attributeFilters_opAbsent() }
	];

	/** Exists/Absent ignore AttributeFilter.value entirely - see AttributeFilterOperator's own remarks (LogFilter.cs). */
	function needsValue(operator: AttributeFilterOperator): boolean {
		return operator === 'Equals' || operator === 'NotEquals';
	}

	interface Row {
		id: number;
		bag: AttributeBag;
		key: string;
		operator: AttributeFilterOperator;
		value: string;
	}

	let nextId = 0;

	function toRows(filters: AttributeFilter[]): Row[] {
		return filters.map((f) => ({ id: nextId++, bag: f.bag, key: f.key, operator: f.operator ?? 'Equals', value: f.value }));
	}

	let rows = $state<Row[]>(toRows(explorer.filter.attributeFilters));

	/** Cheap content snapshot for the resync guard below - order-sensitive is fine, since commit() always writes rows in their current on-screen order. */
	function snapshotOf(filters: AttributeFilter[]): string {
		return JSON.stringify(filters.map((f) => [f.bag, f.key, f.operator ?? 'Equals', f.value]));
	}

	// Tracks the last committed value so the resync effect below can tell "explorer.filter
	// changed because of my own commit()" apart from "explorer.filter changed from outside
	// this component" (applySavedViewState, applyDeepLinkFilter) - only the latter should
	// overwrite `rows`. Without this guard, commit()'s own write bounces straight back
	// through the effect and re-derives `rows` from the *committed* filters - which drops
	// any row with an empty/mid-edit key (see commit()'s own filter), instantly deleting a
	// just-added row the moment its bag/operator is changed before a key is typed.
	let lastAppliedSnapshot = snapshotOf(explorer.filter.attributeFilters);

	$effect(() => {
		const snapshot = snapshotOf(explorer.filter.attributeFilters);
		if (snapshot === lastAppliedSnapshot) return;
		lastAppliedSnapshot = snapshot;
		rows = toRows(explorer.filter.attributeFilters);
	});

	/** Only rows with a non-empty key are sent - a row mid-edit (key not typed yet) shouldn't turn into a nonsensical `""` key filter. */
	function commit(): void {
		const filters: AttributeFilter[] = rows
			.filter((r) => r.key.trim())
			.map((r) => ({
				bag: r.bag,
				key: r.key.trim(),
				operator: r.operator,
				value: needsValue(r.operator) ? r.value : ''
			}));
		lastAppliedSnapshot = snapshotOf(filters);
		explorer.setAttributeFilters(filters);
	}

	let commitDebounce: ReturnType<typeof setTimeout> | undefined;
	function commitDebounced(): void {
		clearTimeout(commitDebounce);
		commitDebounce = setTimeout(commit, 300);
	}

	function addRow(): void {
		rows = [...rows, { id: nextId++, bag: 'Log', key: '', operator: 'Equals', value: '' }];
	}

	function removeRow(id: number): void {
		rows = rows.filter((r) => r.id !== id);
		clearTimeout(commitDebounce);
		commit();
	}

	function updateRow(id: number, patch: Partial<Row>): void {
		rows = rows.map((r) => (r.id === id ? { ...r, ...patch } : r));
	}

	const activeCount = $derived(explorer.filter.attributeFilters.length);

	/**
	 * Value autocomplete for one row's value input (AttributeValueCombobox) - every
	 * distinct value observed for the row's current bag+key, scoped to the same
	 * filter/time-window the log table itself is searching (see
	 * `explorer.buildFilter`/`currentRange`), narrowed by `text` if the caller's already
	 * typed something. Best-effort: a key not yet chosen or a failed lookup both resolve to
	 * no suggestions rather than surfacing an error - same posture
	 * `TracesExplorerState.loadKnownServices` documents for its own best-effort fetch.
	 */
	async function suggestValues(row: Row, text: string, signal: AbortSignal): Promise<AttributeValueSuggestion[]> {
		const key = row.key.trim();
		if (!key) return [];
		try {
			const res = await getLogAttributeValues(
				{
					filter: explorer.buildFilter(explorer.currentRange()),
					bag: row.bag,
					key,
					prefix: text.trim() || undefined,
					limit: 20
				},
				signal
			);
			return res.values;
		} catch {
			return [];
		}
	}
</script>

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-b">
	<Accordion.Item value={ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				{m.attributeFilters_label()}
			</Accordion.Trigger>
			{#if activeCount > 0}
				<span class="text-muted-foreground tabular-nums">{m.attributeFilters_activeCount({ count: activeCount })}</span>
			{/if}
		</div>
		<Accordion.Content class="px-4 pb-3">
			<div class="flex flex-col gap-2">
				{#each rows as row (row.id)}
					<div class="flex flex-wrap items-center gap-1.5">
						<Select.Root
							type="single"
							value={row.bag}
							onValueChange={(v) => {
								if (!v) return;
								updateRow(row.id, { bag: v as AttributeBag });
								commit();
							}}
						>
							<Select.Trigger class="h-7 w-24 text-xs">
								{BAG_OPTIONS.find((o) => o.value === row.bag)?.label}
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
							value={row.key}
							oninput={(e) => {
								updateRow(row.id, { key: e.currentTarget.value });
								commitDebounced();
							}}
						/>

						<Select.Root
							type="single"
							value={row.operator}
							onValueChange={(v) => {
								if (!v) return;
								updateRow(row.id, { operator: v as AttributeFilterOperator });
								commit();
							}}
						>
							<Select.Trigger class="h-7 w-32 text-xs">
								{OPERATOR_OPTIONS.find((o) => o.value === row.operator)?.label}
							</Select.Trigger>
							<Select.Content>
								{#each OPERATOR_OPTIONS as option (option.value)}
									<Select.Item value={option.value} label={option.label} />
								{/each}
							</Select.Content>
						</Select.Root>

						{#if needsValue(row.operator)}
							<AttributeValueCombobox
								class="h-7 w-40 text-xs"
								placeholder={m.attributeFilters_valuePlaceholder()}
								value={row.value}
								oninput={(v) => {
									updateRow(row.id, { value: v });
									commitDebounced();
								}}
								fetchSuggestions={(text, signal) => suggestValues(row, text, signal)}
							/>
						{/if}

						<button
							type="button"
							class="text-muted-foreground hover:text-foreground"
							onclick={() => removeRow(row.id)}
							aria-label={m.attributeFilters_removeFilter()}
						>
							<XIcon class="size-3.5" />
						</button>
					</div>
				{/each}

				<Button variant="outline" size="sm" class="w-fit" onclick={addRow}>
					<PlusIcon data-icon="inline-start" />
					{m.attributeFilters_addFilter()}
				</Button>
			</div>
		</Accordion.Content>
	</Accordion.Item>
</Accordion.Root>
