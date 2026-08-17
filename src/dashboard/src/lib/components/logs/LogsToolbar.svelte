<script lang="ts">
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import TimeRangePicker from './TimeRangePicker.svelte';
	import PopoverMultiSelect from './PopoverMultiSelect.svelte';
	import PatternsModal from './PatternsModal.svelte';
	import SavedSearchesMenu from '$lib/components/logs/SavedSearchesMenu.svelte';
	import RadioIcon from '@lucide/svelte/icons/radio';
	import SearchIcon from '@lucide/svelte/icons/search';
	import SunIcon from '@lucide/svelte/icons/sun';
	import MoonIcon from '@lucide/svelte/icons/moon';
	import XIcon from '@lucide/svelte/icons/x';
	import { mode, toggleMode } from 'mode-watcher';
	import { logsExplorerContext } from '$lib/logs/context';
	import { SEVERITY_BUCKETS, severityNumbersForBucket } from '$lib/logs/severity';

	const explorer = logsExplorerContext.get();

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));
	const severityOptions = SEVERITY_BUCKETS.map((b) => ({ value: b.label, label: b.label }));

	const selectedSeverityLabels = $derived(
		SEVERITY_BUCKETS.filter((b) =>
			severityNumbersForBucket(b).every((n) => explorer.filter.severityNumbers.includes(n))
		).map((b) => b.label)
	);

	function handleSeverityChange(labels: string[]) {
		const numbers = labels.flatMap((l) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.label === l);
			return bucket ? severityNumbersForBucket(bucket) : [];
		});
		explorer.setSeverityNumbers([...new Set(numbers)]);
	}

	let searchDraft = $state(explorer.filter.search);
	let searchDebounce: ReturnType<typeof setTimeout> | undefined;

	// Keeps the input in sync when filter.search changes from outside typing - applying a
	// saved search (applySavedViewState) or a ?view= link hydration both reassign
	// explorer.filter wholesale, which searchDraft's one-time initializer above never sees.
	// Typing itself only ever writes searchDraft directly (below) then debounces into
	// explorer.setSearch, so this effect re-firing once that debounce settles is a
	// same-value no-op, not a fight over who owns the field.
	$effect(() => {
		searchDraft = explorer.filter.search;
	});

	function handleSearchInput(value: string) {
		searchDraft = value;
		clearTimeout(searchDebounce);
		searchDebounce = setTimeout(() => explorer.setSearch(value), 300);
	}

	const liveVariant = $derived(
		explorer.connectionStatus === 'open' ? 'secondary' : explorer.connectionStatus === 'error' ? 'destructive' : 'outline'
	);
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<TimeRangePicker />
	<PopoverMultiSelect
		label="Service"
		options={serviceOptions}
		selected={explorer.filter.services}
		onChange={(next) => explorer.setServices(next)}
	/>
	<PopoverMultiSelect label="Level" options={severityOptions} selected={selectedSeverityLabels} onChange={handleSeverityChange} />

	{#if explorer.filter.patternId}
		<!-- Drill-down from PatternsModal ("View occurrences") - a sticky filter with no
		     other UI control to remove it otherwise, so it needs its own visible, dismissible
		     chip rather than silently narrowing every future search. -->
		<Badge variant="secondary" class="max-w-64 gap-1">
			<span class="truncate font-mono" title={explorer.patternFilterLabel ?? undefined}>{explorer.patternFilterLabel}</span>
			<button
				type="button"
				class="hover:text-foreground shrink-0"
				onclick={() => explorer.clearPatternIdFilter()}
				aria-label="Clear pattern filter"
			>
				<XIcon class="size-3" />
			</button>
		</Badge>
	{/if}

	<SavedSearchesMenu currentState={() => explorer.toSavedViewState()} applyState={(s) => explorer.applySavedViewState(s)} />

	<PatternsModal onSelectPattern={(patternId, template) => explorer.applyPatternIdFilter(patternId, template)} />

	<div class="relative min-w-48 flex-1">
		<SearchIcon class="text-muted-foreground pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2" />
		<Input
			class="pl-8"
			placeholder="Search message body..."
			value={searchDraft}
			oninput={(e) => handleSearchInput(e.currentTarget.value)}
		/>
	</div>

	<!-- Clicking anywhere on the button (including the badge) toggles live via the shared
	     onclick below - the badge is a label, not a separate control. Once actually
	     streaming ("open"), it reads "Pause" so the button communicates what clicking does
	     next, not raw websocket state; connecting/closed/error stay as technical status
	     since those aren't states a click "pauses" out of. -->
	<Button
		variant={explorer.live ? 'default' : 'outline'}
		size="sm"
		onclick={() => explorer.setLive(!explorer.live)}
	>
		<RadioIcon data-icon="inline-start" />
		Live
		{#if explorer.live}
			<Badge variant={liveVariant} class="ml-1">
				{explorer.connectionStatus === 'open' ? 'Pause' : explorer.connectionStatus}
			</Badge>
		{/if}
	</Button>

	<!-- mode.current is undefined during SSR (mode-watcher's isBrowser guard) - the icon
	     briefly defaults to Moon in that window, corrected the instant the client hydrates.
	     Harmless: the anti-FOUC script in +layout.svelte already set the *page's* actual
	     theme correctly before paint, this only affects which icon this one button shows
	     for a frame. -->
	<Button variant="outline" size="icon-sm" onclick={toggleMode} title="Toggle dark/light theme">
		{#if mode.current === 'light'}
			<SunIcon />
		{:else}
			<MoonIcon />
		{/if}
	</Button>
</div>
