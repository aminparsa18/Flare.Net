<script lang="ts">
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { RangeCalendar } from '$lib/components/ui/range-calendar';
	import { getLocalTimeZone, type DateValue } from '@internationalized/date';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import ChevronLeftIcon from '@lucide/svelte/icons/chevron-left';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import { logsExplorerContext } from '$lib/logs/context';
	import {
		TIME_RANGE_PRESETS,
		LOG_ONLY_TIME_RANGE_PRESETS,
		presetLabel,
		type TimeRangePreset
	} from '$lib/logs/time-range';

	const explorer = logsExplorerContext.get();

	// Display order: fixed-duration presets, then the calendar-relative ones, then custom
	// last - same "Last N ... All time / Today / This week ... Custom" shape Seq uses.
	// TIME_RANGE_PRESETS keeps 'custom' at its end for Metrics/Traces (which render it
	// directly, minus 'custom'), so it's pulled out and re-appended here instead.
	const presets: TimeRangePreset[] = [
		...TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom').map((p) => p.value),
		...LOG_ONLY_TIME_RANGE_PRESETS.map((p) => p.value),
		'custom'
	];

	let open = $state(false);
	let showCustom = $state(false);
	let calendarValue = $state<{ start: DateValue | undefined; end: DateValue | undefined }>({
		start: undefined,
		end: undefined
	});

	function formatCustomLabel(range: { from: Date; to: Date } | null): string {
		if (!range) return 'Custom range';
		const fmt = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
		return `${fmt(range.from)} – ${fmt(range.to)}`;
	}

	const activeLabel = $derived(
		explorer.filter.timeRangePreset === 'custom'
			? formatCustomLabel(explorer.filter.customRange)
			: presetLabel(explorer.filter.timeRangePreset)
	);

	function selectPreset(preset: TimeRangePreset) {
		if (preset === 'custom') {
			showCustom = true;
			return;
		}
		explorer.setTimeRangePreset(preset);
		open = false;
		showCustom = false;
	}

	function applyCustomRange() {
		if (!calendarValue.start || !calendarValue.end) return;
		const tz = getLocalTimeZone();
		explorer.setCustomRange({ from: calendarValue.start.toDate(tz), to: calendarValue.end.toDate(tz) });
		open = false;
		showCustom = false;
	}

	// 'all time' has no fixed duration to pan by, and an unresolved 'custom' (no dates
	// picked yet) has nothing to shift from either - see shiftTimeRange's own remarks.
	const canShift = $derived(
		explorer.filter.timeRangePreset !== 'all' &&
			(explorer.filter.timeRangePreset !== 'custom' || explorer.filter.customRange !== null)
	);
</script>

{#if explorer.live}
	<!-- Live mode locks the range to "now" server-side (the tail endpoint ignores from/to) - no point offering a control that can't do anything. -->
	<Button variant="outline" size="sm" disabled>
		<ClockIcon data-icon="inline-start" />
		Live — streaming now
	</Button>
{:else}
	<div class="inline-flex items-center">
		<Button
			variant="outline"
			size="icon-sm"
			class="rounded-r-none"
			disabled={!canShift}
			onclick={() => explorer.shiftTimeRange(-1)}
			title="Shift back"
			aria-label="Shift time range back"
		>
			<ChevronLeftIcon />
		</Button>
		<Popover.Root bind:open>
			<Popover.Trigger>
				{#snippet child({ props })}
					<Button {...props} variant="outline" size="sm" class="-ml-px rounded-none focus-visible:z-10">
						<ClockIcon data-icon="inline-start" />
						{activeLabel}
					</Button>
				{/snippet}
			</Popover.Trigger>
			<Popover.Content class="w-auto p-2" align="start">
				{#if !showCustom}
					<div class="flex flex-col gap-1">
						{#each presets as preset (preset)}
							<Button variant="ghost" size="sm" class="justify-start" onclick={() => selectPreset(preset)}>
								{presetLabel(preset)}
							</Button>
						{/each}
					</div>
				{:else}
					<RangeCalendar bind:value={calendarValue} />
					<div class="flex justify-end gap-2 pt-2">
						<Button variant="ghost" size="sm" onclick={() => (showCustom = false)}>Back</Button>
						<Button size="sm" disabled={!calendarValue.start || !calendarValue.end} onclick={applyCustomRange}>
							Apply
						</Button>
					</div>
				{/if}
			</Popover.Content>
		</Popover.Root>
		<Button
			variant="outline"
			size="icon-sm"
			class="-ml-px rounded-l-none"
			disabled={!canShift}
			onclick={() => explorer.shiftTimeRange(1)}
			title="Shift forward"
			aria-label="Shift time range forward"
		>
			<ChevronRightIcon />
		</Button>
	</div>
{/if}