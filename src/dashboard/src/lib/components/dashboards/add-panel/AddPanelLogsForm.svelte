<script lang="ts">
	// The "Logs" half of AddPanelDialog.svelte - a fresh, dialog-scoped LogsExplorerState
	// (own context, own subtree, disposed on unmount) driving a minimal filter bar built
	// from the same controls LogsToolbar.svelte itself uses (TimeRangePicker,
	// PopoverMultiSelect for services/severity, a debounced search Input) plus a live
	// VolumeChart preview - not the full LogsToolbar, which also wires in Export/Patterns/
	// SavedSearchesMenu/ShareViewButton/PinToDashboardButton/live-tail toggle/command-
	// palette registration, none of which belong in an authoring dialog. See
	// docs-internal/adr/0024-custom-dashboards-phase2-editor.md.
	import { onMount, onDestroy } from 'svelte';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import TimeRangePicker from '$lib/components/logs/TimeRangePicker.svelte';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import { Input } from '$lib/components/ui/input';
	import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.set(new LogsExplorerState());
	explorer.live = false; // this is a query-authoring preview, not a live tail

	onMount(() => void explorer.loadKnownServices());
	onDestroy(() => explorer.dispose());

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));
	const severityOptions = $derived(SEVERITY_BUCKETS.map((b) => ({ value: b.id, label: severityBucketLabel(b) })));
	const selectedSeverityIds = $derived(
		SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => explorer.filter.severityNumbers.includes(n))).map(
			(b) => b.id
		)
	);

	function handleSeverityChange(ids: string[]): void {
		const numbers = ids.flatMap((id) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.id === id);
			return bucket ? severityNumbersForBucket(bucket) : [];
		});
		explorer.setSeverityNumbers([...new Set(numbers)]);
	}

	// Local draft + debounce, same shape LogsToolbar's own search box uses.
	let searchDraft = $state('');
	let searchDebounce: ReturnType<typeof setTimeout> | undefined;
	function handleSearchInput(value: string): void {
		searchDraft = value;
		clearTimeout(searchDebounce);
		searchDebounce = setTimeout(() => explorer.setSearch(value), 300);
	}

	/** Read by AddPanelDialog.svelte (via `bind:this`) at submit time - not called reactively, same "reflect whatever's current when the user actually clicks" convention PinToDashboardDialog's `currentState` prop already documents. */
	export function currentState(): unknown {
		return explorer.toSavedViewState();
	}
</script>

<div class="flex flex-wrap items-center gap-2">
	<TimeRangePicker />
	<PopoverMultiSelect
		label={m.logsToolbar_serviceLabel()}
		options={serviceOptions}
		selected={explorer.filter.services}
		onChange={(next) => explorer.setServices(next)}
	/>
	<PopoverMultiSelect
		label={m.logsToolbar_levelLabel()}
		options={severityOptions}
		selected={selectedSeverityIds}
		onChange={handleSeverityChange}
	/>
	<Input
		class="w-48"
		placeholder={m.logsToolbar_searchPlaceholder()}
		value={searchDraft}
		oninput={(e) => handleSearchInput(e.currentTarget.value)}
	/>
</div>
<div class="h-56 min-h-0 overflow-hidden rounded-md border">
	<VolumeChart />
</div>
