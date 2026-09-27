<script lang="ts">
	// Admin-only editor for a metric's unit/description override (ADR-0065) - gated by the
	// caller. Same icon-triggered mini-form shape as ApdexThresholdPopover.svelte. A blank
	// field means "show what the instrumentation sent", so each field overrides on its own.
	// "Chart as a counter" (ADR-0066) is offered only for a Gauge - it's what it changes.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import * as m from '$lib/paraglide/messages';

	let {
		unit,
		description,
		emittedUnit,
		emittedDescription,
		hasOverride,
		isGauge,
		treatAsCounter,
		onSave,
		onReset
	}: {
		unit: string | null;
		description: string | null;
		emittedUnit: string | null;
		emittedDescription: string | null;
		hasOverride: boolean;
		isGauge: boolean;
		treatAsCounter: boolean;
		onSave: (unit: string | null, description: string | null, treatAsCounter: boolean) => Promise<void>;
		onReset: () => Promise<void>;
	} = $props();

	let open = $state(false);
	let unitValue = $state('');
	let descriptionValue = $state('');
	let counterValue = $state(false);
	let saving = $state(false);
	let error = $state<string | null>(null);

	// Re-seed on every open. A field equal to the emitted value starts blank, so saving
	// without touching it doesn't pin today's emitted value as an override.
	$effect(() => {
		if (open) {
			unitValue = unit != null && unit !== emittedUnit ? unit : '';
			descriptionValue = description != null && description !== emittedDescription ? description : '';
			counterValue = treatAsCounter;
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
		const nextUnit = unitValue.trim() || null;
		const nextDescription = descriptionValue.trim() || null;
		const nextCounter = isGauge && counterValue;
		if (nextUnit == null && nextDescription == null && !nextCounter) {
			// Nothing left to override - that's a reset, not an empty override.
			return hasOverride ? run(onReset) : Promise.resolve(void (open = false));
		}
		return run(() => onSave(nextUnit, nextDescription, nextCounter));
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm" title={m.metricMetadata_title()}>
				<PencilIcon data-icon="inline-start" />
				{m.metricMetadata_edit()}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-80" align="start">
		<p class="mb-1 text-sm font-medium">{m.metricMetadata_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.metricMetadata_hint()}</p>
		<label class="mb-1 block text-xs font-medium" for="metric-metadata-unit">{m.metricMetadata_unitLabel()}</label>
		<Input
			id="metric-metadata-unit"
			class="mb-3 h-8"
			maxlength={64}
			placeholder={emittedUnit ?? m.metricMetadata_notSent()}
			bind:value={unitValue}
			disabled={saving}
		/>
		<label class="mb-1 block text-xs font-medium" for="metric-metadata-description">{m.metricMetadata_descriptionLabel()}</label>
		<Textarea
			id="metric-metadata-description"
			rows={3}
			maxlength={1000}
			placeholder={emittedDescription ?? m.metricMetadata_notSent()}
			bind:value={descriptionValue}
			disabled={saving}
		/>
		{#if isGauge}
			<div class="mt-3 flex items-start gap-2">
				<Checkbox id="metric-metadata-counter" class="mt-0.5" bind:checked={counterValue} disabled={saving} />
				<div>
					<label class="block text-xs font-medium" for="metric-metadata-counter">{m.metricMetadata_treatAsCounter()}</label>
					<p class="text-muted-foreground text-xs">{m.metricMetadata_treatAsCounterHint()}</p>
				</div>
			</div>
		{/if}
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={() => run(onReset)} disabled={saving || !hasOverride}>
				{m.metricMetadata_reset()}
			</Button>
			<Button size="sm" onclick={save} disabled={saving}>{m.metricMetadata_save()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
