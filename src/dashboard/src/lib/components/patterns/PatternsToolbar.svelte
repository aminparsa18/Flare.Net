<script lang="ts">
	import TimeRangePicker from '$lib/components/logs/TimeRangePicker.svelte';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import { patternsContext } from '$lib/patterns/context';
	import { SEVERITY_BUCKETS, severityNumbersForBucket } from '$lib/logs/severity';

	const patterns = patternsContext.get();

	const serviceOptions = $derived(patterns.knownServices.map((s) => ({ value: s, label: s })));
	const severityOptions = SEVERITY_BUCKETS.map((b) => ({ value: b.label, label: b.label }));

	const selectedSeverityLabels = $derived(
		SEVERITY_BUCKETS.filter((b) =>
			severityNumbersForBucket(b).every((n) => patterns.filter.severityNumbers.includes(n))
		).map((b) => b.label)
	);

	function handleSeverityChange(labels: string[]) {
		const numbers = labels.flatMap((l) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.label === l);
			return bucket ? severityNumbersForBucket(bucket) : [];
		});
		patterns.setSeverityNumbers([...new Set(numbers)]);
	}
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<TimeRangePicker
		timeRangePreset={patterns.filter.timeRangePreset}
		customRange={patterns.filter.customRange}
		onSelectPreset={(preset) => patterns.setTimeRangePreset(preset)}
		onSelectCustom={(range) => patterns.setCustomRange(range)}
	/>
	<PopoverMultiSelect
		label="Service"
		options={serviceOptions}
		selected={patterns.filter.services}
		onChange={(next) => patterns.setServices(next)}
	/>
	<PopoverMultiSelect label="Level" options={severityOptions} selected={selectedSeverityLabels} onChange={handleSeverityChange} />
</div>
