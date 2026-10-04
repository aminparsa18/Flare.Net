<script lang="ts">
	// "Around a time…" control for the preset-only Metrics/Traces toolbars: paste or pick one
	// timestamp (display time zone) plus a ± window, and onApply gets the resulting range.
	// The Logs toolbar embeds the same flow inside its own TimeRangePicker popover.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import CrosshairIcon from '@lucide/svelte/icons/crosshair';
	import { displayTimeZone } from '$lib/time/display-zone.svelte';
	import { instantToZoned } from '$lib/time/time-zone';
	import { AROUND_WINDOWS_MS, AROUND_DEFAULT_MS, aroundRange, parseAroundTime } from '$lib/time/around';
	import * as m from '$lib/paraglide/messages';

	let { onApply }: { onApply: (range: { from: Date; to: Date }) => void } = $props();

	let open = $state(false);
	let text = $state('');
	let halfMs = $state<number>(AROUND_DEFAULT_MS);
	const time = $derived(parseAroundTime(text, displayTimeZone.resolved));

	function fillNow() {
		text = instantToZoned(new Date(), displayTimeZone.resolved).replace('T', ' ');
	}

	function apply() {
		if (!time) return;
		onApply(aroundRange(time, halfMs));
		open = false;
	}

	function windowLabel(ms: number): string {
		return ms >= 3_600_000 ? `±${ms / 3_600_000}h` : `±${ms / 60_000}m`;
	}
</script>

<Popover.Root bind:open onOpenChange={(o) => o && !text && fillNow()}>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="icon-sm" title={m.timeRangePicker_around()} aria-label={m.timeRangePicker_around()}>
				<CrosshairIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="flex w-72 flex-col gap-2 p-3" align="start">
		<label class="text-muted-foreground text-xs" for="around-popover-time">
			{m.timeRangePicker_aroundTime({ zone: displayTimeZone.resolved })}
		</label>
		<div class="flex gap-1">
			<Input
				id="around-popover-time"
				bind:value={text}
				placeholder={m.timeRangePicker_aroundPlaceholder()}
				aria-invalid={text.trim() !== '' && !time}
				onkeydown={(e) => e.key === 'Enter' && apply()}
			/>
			<Button variant="outline" size="sm" onclick={fillNow}>{m.timeRangePicker_aroundNow()}</Button>
		</div>
		{#if text.trim() !== '' && !time}
			<span class="text-destructive text-xs">{m.timeRangePicker_aroundInvalid()}</span>
		{/if}
		<span class="text-muted-foreground text-xs">{m.timeRangePicker_aroundWindow()}</span>
		<div class="flex gap-1">
			{#each AROUND_WINDOWS_MS as ms (ms)}
				<Button variant={halfMs === ms ? 'default' : 'outline'} size="sm" class="flex-1" onclick={() => (halfMs = ms)}>
					{windowLabel(ms)}
				</Button>
			{/each}
		</div>
		<div class="flex justify-end">
			<Button size="sm" disabled={!time} onclick={apply}>{m.timeRangePicker_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
