<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Badge } from '$lib/components/ui/badge';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import XIcon from '@lucide/svelte/icons/x';
	import { errorsExplorerContext } from '$lib/errors/context';
	import { TIME_RANGE_PRESETS, presetLabel, formatCustomRangeLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import * as m from '$lib/paraglide/messages';

	const errors = errorsExplorerContext.get();

	// No live-tail / custom-range calendar here - same "only fixed-duration presets make
	// sense" call TracesToolbar's own remarks document for itself; an exceptions rollup is
	// window-bounded the same way a trace search is. The filter can still land on 'custom'
	// from a fired exception alert's link (ErrorsExplorerState.applyDeepLinkState), so the
	// trigger renders that range, same as MetricsToolbar's drag-to-zoom case.
	const presets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');

	const activeLabel = $derived(
		errors.filter.timeRangePreset === 'custom'
			? formatCustomRangeLabel(errors.filter.customRange)
			: presetLabel(errors.filter.timeRangePreset)
	);

	const exceptionTypeLabel = $derived(
		errors.filter.exceptionMessage ? `${errors.filter.exceptionType}: ${errors.filter.exceptionMessage}` : errors.filter.exceptionType
	);

	const serviceOptions = $derived(errors.knownServices.map((s) => ({ value: s, label: s })));
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<Select.Root
		type="single"
		value={errors.filter.timeRangePreset}
		onValueChange={(v) => v && errors.setTimeRangePreset(v as TimeRangePreset)}
	>
		<Select.Trigger class="w-auto">
			<ClockIcon data-icon="inline-start" />
			{activeLabel}
		</Select.Trigger>
		<Select.Content>
			{#each presets as preset (preset.value)}
				<Select.Item value={preset.value} label={presetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>

	<PopoverMultiSelect
		label={m.errorsToolbar_serviceLabel()}
		options={serviceOptions}
		selected={errors.filter.services}
		onChange={(next) => errors.setServices(next)}
	/>

	{#if errors.filter.exceptionType}
		<!-- Set only by a fired exception alert's link - no other control removes it, so it
		     gets its own dismissible chip, same as LogsToolbar's pattern drill-down chip. -->
		<Badge variant="secondary" class="max-w-96 gap-1">
			<span class="truncate font-mono" title={exceptionTypeLabel}>{m.errorsToolbar_exceptionTypeChip({ type: exceptionTypeLabel })}</span>
			<button
				type="button"
				class="hover:text-foreground shrink-0"
				onclick={() => errors.clearExceptionType()}
				aria-label={m.errorsToolbar_removeExceptionType()}
			>
				<XIcon class="size-3" />
			</button>
		</Badge>
	{/if}

	<Button variant="ghost" size="sm" onclick={() => errors.resetFilters()} disabled={!errors.hasActiveFilters()}>
		<XIcon data-icon="inline-start" />
		{m.errorsToolbar_clearFilters()}
	</Button>

	<Button variant="outline" size="sm" class="ml-auto" onclick={() => errors.runSearch()} disabled={errors.loading}>
		{#if errors.loading}
			<Spinner class="size-4" data-icon="inline-start" />
		{:else}
			<RefreshCwIcon data-icon="inline-start" />
		{/if}
		{m.errorsToolbar_refresh()}
	</Button>
</div>
