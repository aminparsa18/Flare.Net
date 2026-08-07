<script lang="ts">
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import TimeRangePicker from './TimeRangePicker.svelte';
	import PopoverMultiSelect from './PopoverMultiSelect.svelte';
	import RadioIcon from '@lucide/svelte/icons/radio';
	import SearchIcon from '@lucide/svelte/icons/search';
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

	<div class="relative min-w-48 flex-1">
		<SearchIcon class="text-muted-foreground pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2" />
		<Input
			class="pl-8"
			placeholder="Search message body..."
			value={searchDraft}
			oninput={(e) => handleSearchInput(e.currentTarget.value)}
		/>
	</div>

	<Button
		variant={explorer.live ? 'default' : 'outline'}
		size="sm"
		onclick={() => explorer.setLive(!explorer.live)}
	>
		<RadioIcon data-icon="inline-start" />
		Live
		{#if explorer.live}
			<Badge variant={liveVariant} class="ml-1">{explorer.connectionStatus}</Badge>
		{/if}
	</Button>
</div>
