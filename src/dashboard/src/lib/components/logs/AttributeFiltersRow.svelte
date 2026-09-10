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
	import type { AttributeBag, AttributeFilter, AttributeFilterOperator } from '$lib/api';
	import * as Accordion from '$lib/components/ui/accordion';
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
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

	// Resyncs when explorer.filter is reassigned wholesale from outside this component
	// (applySavedViewState, applyDeepLinkFilter) - same reasoning LogsToolbar's own
	// searchDraft effect documents. A same-value re-sync right after this component's own
	// commit() is a harmless no-op (new ids, identical bag/key/operator/value content).
	$effect(() => {
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
							<Input
								class="h-7 w-40 text-xs"
								placeholder={m.attributeFilters_valuePlaceholder()}
								value={row.value}
								oninput={(e) => {
									updateRow(row.id, { value: e.currentTarget.value });
									commitDebounced();
								}}
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
