<script lang="ts">
	// Legend placement + per-series color editor for a Metrics panel (`DashboardPanel.
	// legendPosition`/`seriesColors`, see `$lib/dashboards/legend.ts`) - same icon-triggered
	// mini-form shape as ThresholdsPopover.svelte, nothing saved until Apply. Only rendered by
	// DashboardPanelCard.svelte while `editing` a panel whose visualization draws a legend.
	//
	// Lists the series the panel last fetched (`seriesKeys`, reported up by
	// DashboardMetricsPanelBody) plus any saved override whose series isn't in the current
	// result, so a stale override stays visible and can be reset rather than lingering unseen.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { LEGEND_FORMAT_EXAMPLE, LEGEND_POSITIONS, type LegendPosition } from '$lib/dashboards/legend';
	import { THRESHOLD_COLORS, thresholdColorValue, type ThresholdColor } from '$lib/dashboards/thresholds';
	import { thresholdColorLabel } from './threshold-color-label';
	import ListIcon from '@lucide/svelte/icons/list';
	import * as m from '$lib/paraglide/messages';

	let {
		legendPosition,
		seriesColors,
		legendFormat,
		seriesKeys,
		onApply
	}: {
		legendPosition: LegendPosition | undefined;
		seriesColors: Record<string, ThresholdColor>;
		legendFormat: string | undefined;
		seriesKeys: string[];
		onApply: (legendPosition: LegendPosition | undefined, seriesColors: Record<string, ThresholdColor>, legendFormat: string | undefined) => void;
	} = $props();

	/** Select values - bits-ui treats `''` as "nothing selected", so the defaults get a sentinel. */
	const AUTO = 'auto';
	const colorNames = Object.keys(THRESHOLD_COLORS) as ThresholdColor[];

	function positionLabel(position: LegendPosition | typeof AUTO): string {
		switch (position) {
			case AUTO:
				return m.legendPopover_positionAuto();
			case 'bottom':
				return m.legendPopover_positionBottom();
			case 'right':
				return m.legendPopover_positionRight();
			case 'hidden':
				return m.legendPopover_positionHidden();
		}
	}

	let open = $state(false);
	let draftPosition = $state<string>(AUTO);
	let draftColors = $state<Record<string, string>>({});
	let draftFormat = $state('');

	const listedKeys = $derived([...new Set([...seriesKeys, ...Object.keys(seriesColors)])]);
	const hasCustomization = $derived(legendPosition !== undefined || legendFormat !== undefined || Object.keys(seriesColors).length > 0);

	// Re-seeded from the saved values each time this opens - see YAxisBoundsPopover.svelte.
	$effect(() => {
		if (open) {
			draftPosition = legendPosition ?? AUTO;
			draftColors = { ...seriesColors };
			draftFormat = legendFormat ?? '';
		}
	});

	function apply(): void {
		const colors: Record<string, ThresholdColor> = {};
		for (const [key, color] of Object.entries(draftColors)) {
			if (color in THRESHOLD_COLORS) colors[key] = color as ThresholdColor;
		}
		onApply(draftPosition === AUTO ? undefined : (draftPosition as LegendPosition), colors, draftFormat.trim() === '' ? undefined : draftFormat);
		open = false;
	}

	function clear(): void {
		onApply(undefined, {}, undefined);
		open = false;
	}

	function setColor(key: string, value: string): void {
		if (value === AUTO) delete draftColors[key];
		else draftColors[key] = value;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={hasCustomization ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={m.legendPopover_title()}
			>
				<ListIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-96" align="end">
		<p class="mb-3 text-sm font-medium">{m.legendPopover_title()}</p>
		<label class="flex items-center justify-between gap-2 text-xs">
			<span class="text-muted-foreground">{m.legendPopover_position()}</span>
			<Select.Root type="single" bind:value={draftPosition}>
				<Select.Trigger class="h-8 w-32">{positionLabel(draftPosition as LegendPosition | typeof AUTO)}</Select.Trigger>
				<Select.Content>
					<Select.Item value={AUTO} label={positionLabel(AUTO)} />
					{#each LEGEND_POSITIONS as position (position)}
						<Select.Item value={position} label={positionLabel(position)} />
					{/each}
				</Select.Content>
			</Select.Root>
		</label>

		<p class="mt-4 mb-1 text-xs font-medium">{m.legendPopover_format()}</p>
		<p class="text-muted-foreground mb-2 text-xs">{m.legendPopover_formatDescription()}</p>
		<Input class="h-8 font-mono text-xs" bind:value={draftFormat} placeholder={LEGEND_FORMAT_EXAMPLE} aria-label={m.legendPopover_format()} />

		<p class="mt-4 mb-1 text-xs font-medium">{m.legendPopover_seriesColors()}</p>
		<p class="text-muted-foreground mb-2 text-xs">{m.legendPopover_seriesColorsDescription()}</p>
		{#if listedKeys.length === 0}
			<p class="text-muted-foreground text-xs italic">{m.legendPopover_noSeries()}</p>
		{:else}
			<div class="max-h-64 space-y-1.5 overflow-y-auto pr-1">
				{#each listedKeys as key (key)}
					{@const color = draftColors[key]}
					<div class="flex items-center gap-2 text-xs">
						<span class="min-w-0 flex-1 truncate {seriesKeys.includes(key) ? '' : 'text-muted-foreground line-through'}" title={key}>{key}</span>
						<Select.Root type="single" value={color ?? AUTO} onValueChange={(v) => setColor(key, v)}>
							<Select.Trigger class="h-8 w-32 shrink-0" aria-label={m.legendPopover_colorFor({ series: key })}>
								{#if color && color in THRESHOLD_COLORS}
									<span class="inline-block size-3 shrink-0 rounded-full" style="background: {thresholdColorValue(color as ThresholdColor)};"></span>
									{thresholdColorLabel(color as ThresholdColor)}
								{:else}
									{m.legendPopover_colorAuto()}
								{/if}
							</Select.Trigger>
							<Select.Content>
								<Select.Item value={AUTO} label={m.legendPopover_colorAuto()} />
								{#each colorNames as name (name)}
									<Select.Item value={name} label={thresholdColorLabel(name)}>
										<span class="inline-block size-3 rounded-full" style="background: {thresholdColorValue(name)};"></span>
										{thresholdColorLabel(name)}
									</Select.Item>
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
				{/each}
			</div>
		{/if}

		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasCustomization}>{m.legendPopover_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.legendPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
