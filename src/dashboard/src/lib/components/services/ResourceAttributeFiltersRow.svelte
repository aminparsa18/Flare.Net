<script lang="ts">
	// The Services tab's filter chips (docs-internal/planning/roadmap.md's now-removed
	// "Resource-attribute filtering on the Traces > Services tab" item) - narrows the
	// Table view, Map view, and per-node drill-down together via ServicesState's one
	// `resourceAttributes` field. Same Accordion-row shape as Logs'
	// AttributeFiltersRow.svelte / the Traces tab's own SpanAttributeFiltersRow.svelte,
	// simplified: no bag selector (always the Resource bag - see
	// `ResourceAttributeFilter`'s own remarks on why that's the only bag this endpoint
	// shape supports) and no operator selector (equality-only, same reasoning), and a
	// plain text input for the value rather than those two's AttributeValueCombobox -
	// there's no `/api/services/*` attribute-value-autocomplete endpoint yet to back one.
	//
	// Rows are local, ephemeral UI state (each needs a stable key for {#each} that
	// ResourceAttributeFilter itself has no field for) - committed into
	// services.resourceAttributes (and re-fetched) via services.setResourceAttributes
	// whenever a row's committed shape actually changes. Add/remove commit immediately;
	// key/value text inputs debounce (300ms) - same "typing shouldn't fire a request per
	// keystroke" reasoning the two sibling rows already document.
	import { browser } from '$app/environment';
	import { servicesContext } from '$lib/services/context';
	import type { ResourceAttributeFilter } from '$lib/services-api';
	import * as Accordion from '$lib/components/ui/accordion';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.get();

	const ITEM = 'resource-attribute-filters';
	const COLLAPSE_STORAGE_KEY = 'flare.services.resourceAttributeFiltersCollapsed';

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

	interface Row {
		id: number;
		key: string;
		value: string;
	}

	let nextId = 0;

	function toRows(filters: ResourceAttributeFilter[]): Row[] {
		return filters.map((f) => ({ id: nextId++, key: f.key, value: f.value }));
	}

	let rows = $state<Row[]>(toRows(services.resourceAttributes));

	/** Cheap content snapshot for the resync guard below - same purpose as AttributeFiltersRow.svelte's own `snapshotOf`. */
	function snapshotOf(filters: ResourceAttributeFilter[]): string {
		return JSON.stringify(filters.map((f) => [f.key, f.value]));
	}

	// Tells "services.resourceAttributes changed because of my own commit()" apart from
	// "changed from outside this component" - same guard, same reason (dropping a
	// mid-edit row otherwise) as AttributeFiltersRow.svelte's own `lastAppliedSnapshot`.
	let lastAppliedSnapshot = snapshotOf(services.resourceAttributes);

	$effect(() => {
		const snapshot = snapshotOf(services.resourceAttributes);
		if (snapshot === lastAppliedSnapshot) return;
		lastAppliedSnapshot = snapshot;
		rows = toRows(services.resourceAttributes);
	});

	/** Only rows with a non-empty key are sent - a row mid-edit (key not typed yet) shouldn't turn into a nonsensical `""` key filter. */
	function commit(): void {
		const filters: ResourceAttributeFilter[] = rows.filter((r) => r.key.trim()).map((r) => ({ key: r.key.trim(), value: r.value }));
		lastAppliedSnapshot = snapshotOf(filters);
		services.setResourceAttributes(filters);
	}

	let commitDebounce: ReturnType<typeof setTimeout> | undefined;
	function commitDebounced(): void {
		clearTimeout(commitDebounce);
		commitDebounce = setTimeout(commit, 300);
	}

	function addRow(): void {
		rows = [...rows, { id: nextId++, key: '', value: '' }];
	}

	function removeRow(id: number): void {
		rows = rows.filter((r) => r.id !== id);
		clearTimeout(commitDebounce);
		commit();
	}

	function updateRow(id: number, patch: Partial<Row>): void {
		rows = rows.map((r) => (r.id === id ? { ...r, ...patch } : r));
	}

	const activeCount = $derived(services.resourceAttributes.length);
</script>

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-b">
	<Accordion.Item value={ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				{m.resourceAttributeFilters_label()}
			</Accordion.Trigger>
			{#if activeCount > 0}
				<span class="text-muted-foreground tabular-nums">{m.attributeFilters_activeCount({ count: activeCount })}</span>
			{/if}
		</div>
		<Accordion.Content class="px-4 pb-3">
			<div class="flex flex-col gap-2">
				{#each rows as row (row.id)}
					<div class="flex flex-wrap items-center gap-1.5">
						<Input
							class="h-7 w-40 text-xs"
							placeholder={m.attributeFilters_keyPlaceholder()}
							value={row.key}
							oninput={(e) => {
								updateRow(row.id, { key: e.currentTarget.value });
								commitDebounced();
							}}
						/>

						<Input
							class="h-7 w-40 text-xs"
							placeholder={m.attributeFilters_valuePlaceholder()}
							value={row.value}
							oninput={(e) => {
								updateRow(row.id, { value: e.currentTarget.value });
								commitDebounced();
							}}
						/>

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
