<script lang="ts">
	// Per-service Apdex threshold (T) editor - Admin-only (gated by the caller, see
	// ServicesTable.svelte), backed by ApdexThresholdEndpoints/SqliteApdexThresholdStore.
	// Same "small icon-triggered popover with a mini form" shape as
	// PanelVariablesPopover.svelte, the closest existing precedent for a per-row settings
	// affordance in this codebase - see docs-internal/adr/0032-apdex-score-per-service.md.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import SettingsIcon from '@lucide/svelte/icons/settings-2';
	import * as m from '$lib/paraglide/messages';

	let {
		serviceName,
		currentThresholdMs,
		defaultThresholdMs,
		hasOverride,
		onSave,
		onReset
	}: {
		serviceName: string;
		currentThresholdMs: number;
		defaultThresholdMs: number;
		hasOverride: boolean;
		onSave: (thresholdMs: number) => Promise<void>;
		onReset: () => Promise<void>;
	} = $props();

	let open = $state(false);
	// Set for real by the $effect below whenever this is opened - starts empty since
	// referencing the currentThresholdMs prop here would only capture its initial value,
	// not stay reactive to later prop changes (svelte's state_referenced_locally).
	let inputValue = $state('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	// Re-seed from the latest server value each time this is opened - a stale value left
	// over from a previous open (e.g. left open across a poll that changed nothing, then
	// closed and reopened later) shouldn't show.
	$effect(() => {
		if (open) {
			inputValue = String(currentThresholdMs);
			error = null;
		}
	});

	async function save(): Promise<void> {
		const parsed = Number(inputValue);
		if (!Number.isInteger(parsed) || parsed < 1) {
			error = m.apdexThresholdPopover_invalidValue();
			return;
		}
		saving = true;
		error = null;
		try {
			await onSave(parsed);
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}

	async function reset(): Promise<void> {
		saving = true;
		error = null;
		try {
			await onReset();
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={hasOverride ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={m.apdexThresholdPopover_title()}
			>
				<SettingsIcon class="size-3.5" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64" align="end">
		<p class="mb-1 text-sm font-medium">{m.apdexThresholdPopover_titleForService({ serviceName })}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.apdexThresholdPopover_description({ defaultMs: defaultThresholdMs })}</p>
		<div class="flex items-center gap-2">
			<Input type="number" min="1" bind:value={inputValue} class="h-8" disabled={saving} />
			<span class="text-muted-foreground text-xs">ms</span>
		</div>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={reset} disabled={saving || !hasOverride}>
				{m.apdexThresholdPopover_resetToDefault()}
			</Button>
			<Button size="sm" onclick={save} disabled={saving}>
				{m.apdexThresholdPopover_save()}
			</Button>
		</div>
	</Popover.Content>
</Popover.Root>
