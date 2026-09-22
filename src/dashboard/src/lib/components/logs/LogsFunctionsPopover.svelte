<script lang="ts">
	// Per-query post-processing functions (roadmap: "Per-query post-processing functions
	// (metrics and logs)", ADR-0041) - the Logs-explorer counterpart to
	// MetricsFunctionsPopover.svelte (ADR-0038/0039): a chainable list of app-side transforms
	// (clamp-min/max, absolute, log2/log10, cumulative-sum, EWMA/median smoothing) applied in
	// order to VolumeChart's bucket counts server-side (LogPostProcessor.cs). Deliberately a
	// near-verbatim copy of MetricsFunctionsPopover rather than a shared generic component -
	// see that file's own remarks for the value/windowSize input-coercion gotcha this copy
	// inherits the fix for.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import SquareFunctionIcon from '@lucide/svelte/icons/square-function';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';
	import type { LogPostProcessFunction, LogPostProcessFunctionType } from '$lib/api';

	let {
		functions,
		onApply
	}: {
		functions: LogPostProcessFunction[];
		onApply: (functions: LogPostProcessFunction[]) => void;
	} = $props();

	// Same "closed, sane set of choices, capped" reasoning MetricsFunctionsPopover's own
	// MAX_FUNCTIONS documents.
	const MAX_FUNCTIONS = 5;

	// Same "N-period" default LogPostProcessor.cs' EWMA conversion (alpha = 2/(N+1)) assumes.
	const DEFAULT_WINDOW_SIZE = 5;

	const FUNCTION_OPTIONS: { value: LogPostProcessFunctionType; label: () => string }[] = [
		{ value: 'ClampMin', label: m.logsFunctions_clampMin },
		{ value: 'ClampMax', label: m.logsFunctions_clampMax },
		{ value: 'Absolute', label: m.logsFunctions_absolute },
		{ value: 'Log2', label: m.logsFunctions_log2 },
		{ value: 'Log10', label: m.logsFunctions_log10 },
		{ value: 'CumulativeSum', label: m.logsFunctions_cumulativeSum },
		{ value: 'EwmaSmoothing', label: m.logsFunctions_ewmaSmoothing },
		{ value: 'MedianSmoothing', label: m.logsFunctions_medianSmoothing }
	];

	function needsValue(type: LogPostProcessFunctionType): boolean {
		return type === 'ClampMin' || type === 'ClampMax';
	}

	function needsWindowSize(type: LogPostProcessFunctionType): boolean {
		return type === 'EwmaSmoothing' || type === 'MedianSmoothing';
	}

	let open = $state(false);
	// Re-seeded from the latest applied value each time this is opened - same reactive
	// re-seed MetricsFunctionsPopover's own `$effect` uses, for the same
	// `state_referenced_locally` reason.
	let draft = $state<{ type: LogPostProcessFunctionType; value: string | number; windowSize: string | number }[]>([]);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			draft = functions.map((f) => ({
				type: f.type,
				value: f.value != null ? String(f.value) : '',
				windowSize: f.windowSize != null ? String(f.windowSize) : ''
			}));
			error = null;
		}
	});

	const hasOverride = $derived(functions.length > 0);

	function addRow(): void {
		if (draft.length >= MAX_FUNCTIONS) return;
		draft = [...draft, { type: 'Absolute', value: '', windowSize: '' }];
	}

	function removeRow(index: number): void {
		draft = draft.filter((_, i) => i !== index);
	}

	function setRowType(index: number, type: LogPostProcessFunctionType): void {
		const windowSize =
			needsWindowSize(type) && draft[index].windowSize === '' ? DEFAULT_WINDOW_SIZE : draft[index].windowSize;
		draft[index] = { ...draft[index], type, windowSize };
	}

	function apply(): void {
		const resolved: LogPostProcessFunction[] = [];
		for (const row of draft) {
			if (needsValue(row.type)) {
				const valueText = String(row.value);
				const parsed = Number(valueText);
				if (valueText.trim() === '' || !Number.isFinite(parsed)) {
					error = m.logsFunctions_invalidValue();
					return;
				}
				resolved.push({ type: row.type, value: parsed });
				continue;
			}
			if (needsWindowSize(row.type)) {
				const windowText = String(row.windowSize);
				const parsedWindow = Number(windowText);
				if (windowText.trim() === '' || !Number.isInteger(parsedWindow) || parsedWindow < 1) {
					error = m.logsFunctions_invalidWindowSize();
					return;
				}
				resolved.push({ type: row.type, windowSize: parsedWindow });
				continue;
			}
			resolved.push({ type: row.type });
		}
		onApply(resolved);
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
				variant="outline"
				size="sm"
				class={hasOverride ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}
				title={m.logsFunctions_title()}
			>
				<SquareFunctionIcon class="size-3.5" data-icon="inline-start" />
				{hasOverride ? m.logsFunctions_activeLabel({ count: functions.length }) : m.logsFunctions_label()}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-80" align="start">
		<p class="mb-1 text-sm font-medium">{m.logsFunctions_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.logsFunctions_description()}</p>
		<div class="flex flex-col gap-2">
			{#each draft as row, index (index)}
				<div class="flex items-center gap-2">
					<Select.Root
						type="single"
						value={row.type}
						onValueChange={(v) => v && setRowType(index, v as LogPostProcessFunctionType)}
					>
						<Select.Trigger class="h-8 flex-1">
							{FUNCTION_OPTIONS.find((o) => o.value === row.type)?.label()}
						</Select.Trigger>
						<Select.Content>
							{#each FUNCTION_OPTIONS as option (option.value)}
								<Select.Item value={option.value} label={option.label()} />
							{/each}
						</Select.Content>
					</Select.Root>
					{#if needsValue(row.type)}
						<Input type="number" bind:value={row.value} class="h-8 w-20" />
					{:else if needsWindowSize(row.type)}
						<Input
							type="number"
							min="1"
							step="1"
							bind:value={row.windowSize}
							class="h-8 w-20"
							title={m.logsFunctions_windowSizeTitle()}
							placeholder={m.logsFunctions_windowSizePlaceholder()}
						/>
					{/if}
					<Button variant="ghost" size="icon" class="size-8 shrink-0" onclick={() => removeRow(index)} title={m.logsFunctions_remove()}>
						<XIcon class="size-3.5" />
					</Button>
				</div>
			{/each}
		</div>
		<Button variant="outline" size="sm" class="mt-2 w-full" onclick={addRow} disabled={draft.length >= MAX_FUNCTIONS}>
			<PlusIcon class="size-3.5" data-icon="inline-start" />
			{m.logsFunctions_addFunction()}
		</Button>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasOverride && draft.length === 0}>{m.logsFunctions_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.logsFunctions_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
