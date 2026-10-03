<script lang="ts">
	// Admin-only editor for one model's price per million tokens (ADR-0101) - gated by the
	// caller. Same icon-triggered mini-form shape as MetricMetadataOverridePopover.svelte.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import * as m from '$lib/paraglide/messages';

	let {
		model,
		inputPrice,
		outputPrice,
		isCustom,
		onSave,
		onReset
	}: {
		model: string;
		inputPrice: number | null;
		outputPrice: number | null;
		isCustom: boolean;
		onSave: (input: number, output: number) => Promise<void>;
		onReset: () => Promise<void>;
	} = $props();

	let open = $state(false);
	// `bind:value` on a number input yields a number (null while empty), not a string.
	let inputValue = $state<number | null>(null);
	let outputValue = $state<number | null>(null);
	let saving = $state(false);
	let error = $state<string | null>(null);

	// Re-seed on every open from the price currently in effect.
	$effect(() => {
		if (open) {
			inputValue = inputPrice;
			outputValue = outputPrice;
			error = null;
		}
	});

	async function run(action: () => Promise<void>): Promise<void> {
		saving = true;
		error = null;
		try {
			await action();
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}

	function save(): Promise<void> {
		const input = inputValue;
		const output = outputValue;
		if (input == null || output == null || !Number.isFinite(input) || !Number.isFinite(output) || input < 0 || output < 0) {
			error = m.llmPage_priceInvalid();
			return Promise.resolve();
		}
		return run(() => onSave(input, output));
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="ghost" size="icon-sm" title={m.llmPage_priceEdit()} aria-label={m.llmPage_priceEdit()}>
				<PencilIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-72" align="end">
		<p class="mb-1 text-sm font-medium">{m.llmPage_priceTitle()}</p>
		<p class="text-muted-foreground mb-1 truncate text-xs" title={model}>{model}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.llmPage_priceHint()}</p>
		<label class="mb-1 block text-xs font-medium" for="llm-price-input">{m.llmPage_priceInput()}</label>
		<Input id="llm-price-input" class="mb-3 h-8" type="number" min="0" step="any" bind:value={inputValue} disabled={saving} />
		<label class="mb-1 block text-xs font-medium" for="llm-price-output">{m.llmPage_priceOutput()}</label>
		<Input id="llm-price-output" class="h-8" type="number" min="0" step="any" bind:value={outputValue} disabled={saving} />
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={() => run(onReset)} disabled={saving || !isCustom}>
				{m.llmPage_priceReset()}
			</Button>
			<Button size="sm" onclick={save} disabled={saving}>{m.llmPage_priceSave()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
