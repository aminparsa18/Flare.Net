<script lang="ts">
	// Time-shift overlay for the Logs Explorer's volume chart (roadmap: "Per-query
	// post-processing functions (metrics and logs)", ADR-0042) - the Logs-explorer
	// counterpart to MetricsTimeShiftPopover.svelte (ADR-0040): re-runs VolumeChart's
	// aggregate fetch at a fixed, arbitrary offset and overlays the result as a dashed
	// line over the bars, aligned to the same x-axis. Deliberately a near-verbatim copy of
	// MetricsTimeShiftPopover rather than a shared generic component - same "no
	// per-explorer gating concern to abstract around" reasoning LogsFunctionsPopover's own
	// remarks give for copying MetricsFunctionsPopover. Unlike Metrics' switch, Logs has no
	// existing overlay control to be mutually exclusive with, so there's no clearing logic
	// to carry over.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import HistoryIcon from '@lucide/svelte/icons/history';
	import { formatBucketWidthSeconds } from '$lib/logs/bucket-width';
	import * as m from '$lib/paraglide/messages';

	let {
		timeShiftSeconds,
		onApply
	}: {
		timeShiftSeconds: number | null;
		onApply: (seconds: number | null) => void;
	} = $props();

	const SHIFT_OFF = '__off__';
	const SHIFT_CUSTOM = '__custom__';

	type CustomUnit = 'hours' | 'days';

	const PRESETS: { value: string; seconds: number; label: () => string }[] = [
		{ value: '3600', seconds: 3600, label: m.logsTimeShift_1h },
		{ value: '86400', seconds: 86400, label: m.logsTimeShift_24h },
		{ value: '604800', seconds: 604800, label: m.logsTimeShift_7d }
	];

	function secondsFromCustom(value: number, unit: CustomUnit): number {
		return Math.round(unit === 'days' ? value * 86400 : value * 3600);
	}

	let open = $state(false);
	// Re-seeded from the latest applied value each time this is opened - same reactive
	// re-seed MetricsFunctionsPopover.svelte's own `$effect` uses, for the same
	// `state_referenced_locally` reason.
	let draft = $state(SHIFT_OFF);
	// `customValue` starts life as a string but Svelte's native `bind:value` on
	// `<input type="number">` (inside Input.svelte) coerces it to a real `number` the
	// moment a valid numeric value lands - same gotcha MetricsFunctionsPopover.svelte's
	// own draft rows document, so `apply()` below always goes through `String(...)` first
	// rather than assuming it stayed a string.
	let customValue = $state<string | number>('');
	let customUnit = $state<CustomUnit>('hours');
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			const preset = PRESETS.find((p) => p.seconds === timeShiftSeconds);
			if (timeShiftSeconds == null) {
				draft = SHIFT_OFF;
			} else if (preset) {
				draft = preset.value;
			} else {
				// A custom offset from an earlier apply (or a saved view) that doesn't land on
				// exactly one of the three presets - reverse it into a value+unit pair rather
				// than falling back to "Off" and silently discarding it. Days when it divides
				// evenly (whole-day offsets are the common custom case), hours otherwise -
				// either round-trips exactly through secondsFromCustom since both units above
				// compute in whole seconds.
				draft = SHIFT_CUSTOM;
				if (timeShiftSeconds % 86400 === 0) {
					customUnit = 'days';
					customValue = String(timeShiftSeconds / 86400);
				} else {
					customUnit = 'hours';
					customValue = String(timeShiftSeconds / 3600);
				}
			}
			error = null;
		}
	});

	const hasOverride = $derived(timeShiftSeconds != null);
	const activeDuration = $derived(timeShiftSeconds != null ? formatBucketWidthSeconds(timeShiftSeconds) : '');

	function apply(): void {
		if (draft === SHIFT_OFF) {
			onApply(null);
			open = false;
			return;
		}
		if (draft === SHIFT_CUSTOM) {
			const valueText = String(customValue);
			const parsed = Number(valueText);
			if (valueText.trim() === '' || !Number.isFinite(parsed) || parsed <= 0) {
				error = m.logsTimeShift_invalidValue();
				return;
			}
			onApply(secondsFromCustom(parsed, customUnit));
			open = false;
			return;
		}
		const preset = PRESETS.find((p) => p.value === draft);
		onApply(preset?.seconds ?? null);
		open = false;
	}

	function clear(): void {
		onApply(null);
		open = false;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="outline"
				size="sm"
				class={hasOverride ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}
				title={m.logsTimeShift_title()}
			>
				<HistoryIcon class="size-3.5" data-icon="inline-start" />
				{hasOverride ? m.logsTimeShift_activeLabel({ duration: activeDuration }) : m.logsTimeShift_label()}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-72" align="start">
		<p class="mb-1 text-sm font-medium">{m.logsTimeShift_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.logsTimeShift_description()}</p>
		<div class="flex flex-col gap-2">
			<Select.Root type="single" value={draft} onValueChange={(v) => v && (draft = v)}>
				<Select.Trigger class="h-8 w-full">
					{draft === SHIFT_OFF
						? m.logsTimeShift_off()
						: draft === SHIFT_CUSTOM
							? m.logsTimeShift_custom()
							: PRESETS.find((p) => p.value === draft)?.label()}
				</Select.Trigger>
				<Select.Content>
					<Select.Item value={SHIFT_OFF} label={m.logsTimeShift_off()} />
					{#each PRESETS as preset (preset.value)}
						<Select.Item value={preset.value} label={preset.label()} />
					{/each}
					<Select.Item value={SHIFT_CUSTOM} label={m.logsTimeShift_custom()} />
				</Select.Content>
			</Select.Root>
			{#if draft === SHIFT_CUSTOM}
				<div class="flex items-center gap-2">
					<Input
						type="number"
						min="1"
						step="1"
						bind:value={customValue}
						class="h-8 flex-1"
						placeholder={m.logsTimeShift_customValuePlaceholder()}
					/>
					<Select.Root type="single" value={customUnit} onValueChange={(v) => v && (customUnit = v as CustomUnit)}>
						<Select.Trigger class="h-8 w-28">
							{customUnit === 'days' ? m.logsTimeShift_unitDays() : m.logsTimeShift_unitHours()}
						</Select.Trigger>
						<Select.Content>
							<Select.Item value="hours" label={m.logsTimeShift_unitHours()} />
							<Select.Item value="days" label={m.logsTimeShift_unitDays()} />
						</Select.Content>
					</Select.Root>
				</div>
			{/if}
		</div>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasOverride && draft === SHIFT_OFF}>{m.logsTimeShift_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.logsTimeShift_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
