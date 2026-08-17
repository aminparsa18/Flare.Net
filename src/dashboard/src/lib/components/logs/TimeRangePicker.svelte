<script lang="ts">
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { RangeCalendar } from '$lib/components/ui/range-calendar';
	import { getLocalTimeZone, type DateValue } from '@internationalized/date';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { logsExplorerContext } from '$lib/logs/context';
	import { TIME_RANGE_PRESETS, type TimeRangePreset } from '$lib/logs/time-range';

	const explorer = logsExplorerContext.get();

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
			: (TIME_RANGE_PRESETS.find((p) => p.value === explorer.filter.timeRangePreset)?.label ?? 'Time range')
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
</script>

{#if explorer.live}
	<!-- Live mode locks the range to "now" server-side (the tail endpoint ignores from/to) - no point offering a control that can't do anything. -->
	<Button variant="outline" size="sm" disabled>
		<ClockIcon data-icon="inline-start" />
		Live — streaming now
	</Button>
{:else}
	<Popover.Root bind:open>
		<Popover.Trigger>
			{#snippet child({ props })}
				<Button {...props} variant="outline" size="sm">
					<ClockIcon data-icon="inline-start" />
					{activeLabel}
				</Button>
			{/snippet}
		</Popover.Trigger>
		<Popover.Content class="w-auto p-2" align="start">
			{#if !showCustom}
				<div class="flex flex-col gap-1">
					{#each TIME_RANGE_PRESETS as preset (preset.value)}
						<Button variant="ghost" size="sm" class="justify-start" onclick={() => selectPreset(preset.value)}>
							{preset.label}
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
{/if}