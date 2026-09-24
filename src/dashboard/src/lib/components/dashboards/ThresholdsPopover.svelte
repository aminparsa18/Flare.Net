<script lang="ts">
	// Per-panel visual threshold editor (roadmap's "Per-panel visual thresholds /
	// conditional formatting" item) - same "small icon-triggered popover with a mini form"
	// shape as YAxisBoundsPopover.svelte, and Metrics-only for the same reason (see
	// DashboardPanelCard.svelte's own gating). Edits a local draft of the ordered rule list;
	// nothing is saved until Apply, since every save is a full dashboard PUT.
	//
	// Row order *is* precedence (first match wins - see `$lib/dashboards/thresholds.ts`), so
	// rows get explicit move up/down buttons rather than leaving order implicit.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import {
		THRESHOLD_COLORS,
		THRESHOLD_OPERATORS,
		thresholdColorValue,
		type PanelThreshold,
		type ThresholdColor,
		type ThresholdOperator
	} from '$lib/dashboards/thresholds';
	import PaletteIcon from '@lucide/svelte/icons/palette';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import * as m from '$lib/paraglide/messages';

	let {
		thresholds,
		onApply
	}: {
		/** This panel's own `DashboardPanel.thresholds`, or `undefined`/`[]` for none. */
		thresholds: PanelThreshold[] | undefined;
		onApply: (thresholds: PanelThreshold[]) => void;
	} = $props();

	/** One editable row - `value` is `string | number` for the same `bind:value` on
	 *  `type="number"` coercion reason YAxisBoundsPopover.svelte's drafts document. */
	interface DraftRow {
		id: string;
		operator: ThresholdOperator;
		value: string | number;
		color: ThresholdColor;
	}

	const colorNames = Object.keys(THRESHOLD_COLORS) as ThresholdColor[];

	function colorLabel(color: ThresholdColor): string {
		switch (color) {
			case 'red':
				return m.thresholdsPopover_colorRed();
			case 'orange':
				return m.thresholdsPopover_colorOrange();
			case 'yellow':
				return m.thresholdsPopover_colorYellow();
			case 'green':
				return m.thresholdsPopover_colorGreen();
			case 'blue':
				return m.thresholdsPopover_colorBlue();
			case 'purple':
				return m.thresholdsPopover_colorPurple();
		}
	}

	let open = $state(false);
	let rows = $state<DraftRow[]>([]);
	let error = $state<string | null>(null);

	// Re-seeded from the latest saved rules each time this is opened - same reasoning as
	// YAxisBoundsPopover.svelte's identical `$effect`.
	$effect(() => {
		if (open) {
			rows = (thresholds ?? []).map((t) => ({ ...t, value: String(t.value) }));
			error = null;
		}
	});

	const count = $derived(thresholds?.length ?? 0);

	function addRow(): void {
		// Cycles through the palette so consecutive new rules don't all start the same color.
		const color = colorNames[rows.length % colorNames.length];
		rows.push({ id: crypto.randomUUID(), operator: '>', value: '', color });
	}

	function removeRow(index: number): void {
		rows.splice(index, 1);
	}

	function moveRow(index: number, delta: -1 | 1): void {
		const target = index + delta;
		if (target < 0 || target >= rows.length) return;
		[rows[index], rows[target]] = [rows[target], rows[index]];
	}

	function apply(): void {
		const next: PanelThreshold[] = [];
		for (const row of rows) {
			const value = row.value === '' ? NaN : Number(row.value);
			if (!Number.isFinite(value)) {
				error = m.thresholdsPopover_invalidValue();
				return;
			}
			next.push({ id: row.id, operator: row.operator, value, color: row.color });
		}
		onApply(next);
		open = false;
	}

	function clear(): void {
		onApply([]);
		open = false;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={count > 0 ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={count > 0 ? m.thresholdsPopover_titleWithCount({ count }) : m.thresholdsPopover_title()}
			>
				<PaletteIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-96" align="end">
		<p class="mb-1 text-sm font-medium">{m.thresholdsPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.thresholdsPopover_description()}</p>
		{#if rows.length === 0}
			<p class="text-muted-foreground mb-2 text-xs italic">{m.thresholdsPopover_empty()}</p>
		{:else}
			<div class="space-y-2">
				{#each rows as row, i (row.id)}
					<div class="flex items-center gap-1.5">
						<Select.Root type="single" bind:value={row.operator}>
							<Select.Trigger class="h-8 w-16 shrink-0" aria-label={m.thresholdsPopover_operator()}>{row.operator}</Select.Trigger>
							<Select.Content>
								{#each THRESHOLD_OPERATORS as op (op)}
									<Select.Item value={op} label={op} />
								{/each}
							</Select.Content>
						</Select.Root>
						<Input type="number" bind:value={row.value} aria-label={m.thresholdsPopover_value()} class="h-8 min-w-0 flex-1" />
						<Select.Root type="single" bind:value={row.color}>
							<Select.Trigger class="h-8 w-14 shrink-0" aria-label={m.thresholdsPopover_color()}>
								<span class="inline-block size-3 rounded-full" style="background: {thresholdColorValue(row.color)};"></span>
							</Select.Trigger>
							<Select.Content>
								{#each colorNames as color (color)}
									<Select.Item value={color} label={colorLabel(color)}>
										<span class="inline-block size-3 rounded-full" style="background: {thresholdColorValue(color)};"></span>
										{colorLabel(color)}
									</Select.Item>
								{/each}
							</Select.Content>
						</Select.Root>
						<Button
							variant="ghost"
							size="icon-sm"
							class="shrink-0"
							title={m.thresholdsPopover_moveUp()}
							disabled={i === 0}
							onclick={() => moveRow(i, -1)}
						>
							<ArrowUpIcon />
						</Button>
						<Button
							variant="ghost"
							size="icon-sm"
							class="shrink-0"
							title={m.thresholdsPopover_moveDown()}
							disabled={i === rows.length - 1}
							onclick={() => moveRow(i, 1)}
						>
							<ArrowDownIcon />
						</Button>
						<Button
							variant="ghost"
							size="icon-sm"
							class="text-muted-foreground hover:text-destructive shrink-0"
							title={m.thresholdsPopover_remove()}
							onclick={() => removeRow(i)}
						>
							<XIcon />
						</Button>
					</div>
				{/each}
			</div>
			<p class="text-muted-foreground mt-2 text-xs">{m.thresholdsPopover_precedenceHint()}</p>
		{/if}
		<Button variant="outline" size="sm" class="mt-2" onclick={addRow}>
			<PlusIcon />
			{m.thresholdsPopover_add()}
		</Button>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={count === 0}>{m.thresholdsPopover_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.thresholdsPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
