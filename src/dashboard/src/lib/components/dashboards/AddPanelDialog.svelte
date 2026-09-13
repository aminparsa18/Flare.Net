<script lang="ts">
	// "Add panel" - the in-editor counterpart to PinToDashboardDialog.svelte's "pin from an
	// Explorer page" flow (see docs-internal/adr/0024-custom-dashboards-phase2-editor.md).
	// Picking a panel type mounts that type's own dialog-scoped mini filter bar + live
	// preview (AddPanelLogsForm/AddPanelTracesForm/AddPanelMetricsForm) - each owns its own
	// fresh explorer state and Svelte context, set at that component's own init time, so
	// switching types can't just swap out one shared context value (Svelte's setContext
	// only works during a component's own initialization, not from a click handler) -
	// three small components, mounted/unmounted by the `{#if panelType === ...}` below,
	// achieve the same "start from a blank query" reset that effect-driven forms like
	// PinToDashboardDialog get from re-running an `$effect` on `open`.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import AddPanelLogsForm from './add-panel/AddPanelLogsForm.svelte';
	import AddPanelTracesForm from './add-panel/AddPanelTracesForm.svelte';
	import AddPanelMetricsForm from './add-panel/AddPanelMetricsForm.svelte';
	import { nextPanelPosition } from '$lib/dashboards/layout';
	import type { DashboardPanel, PanelType } from '$lib/dashboards-api';
	import * as m from '$lib/paraglide/messages';

	let {
		open = $bindable(false),
		existingPanels,
		onAdd
	}: {
		open: boolean;
		existingPanels: DashboardPanel[];
		onAdd: (panel: DashboardPanel) => void;
	} = $props();

	const PANEL_TYPES: PanelType[] = ['Logs', 'Traces', 'Metrics'];

	function panelTypeLabel(type: PanelType): string {
		switch (type) {
			case 'Logs':
				return m.nav_logs();
			case 'Traces':
				return m.nav_traces();
			case 'Metrics':
				return m.nav_metrics();
		}
	}

	let panelType = $state<PanelType | null>(null);
	let title = $state('');
	let metricsValid = $state(false);

	let logsForm: AddPanelLogsForm | undefined = $state();
	let tracesForm: AddPanelTracesForm | undefined = $state();
	let metricsForm: AddPanelMetricsForm | undefined = $state();

	function selectType(type: PanelType): void {
		panelType = type;
		title = panelTypeLabel(type); // a sensible starting point - freely editable below, same "prefilled, not locked" convention PinToDashboardDialog's own title input uses
	}

	const canSubmit = $derived(panelType !== null && title.trim() !== '' && (panelType !== 'Metrics' || metricsValid));

	function reset(): void {
		panelType = null;
		title = '';
		metricsValid = false;
	}

	function handleOpenChange(next: boolean): void {
		open = next;
		if (!next) reset();
	}

	function handleSubmit(event: SubmitEvent): void {
		event.preventDefault();
		if (!panelType) return;
		const query =
			panelType === 'Logs'
				? logsForm?.currentState()
				: panelType === 'Traces'
					? tracesForm?.currentState()
					: metricsForm?.currentState();
		onAdd({
			id: crypto.randomUUID(),
			panelType,
			title: title.trim(),
			layout: nextPanelPosition(existingPanels),
			query
		});
		handleOpenChange(false);
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{m.addPanelDialog_title()}</Dialog.Title>
			<Dialog.Description>{m.addPanelDialog_description()}</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="flex gap-2">
				{#each PANEL_TYPES as type (type)}
					<Button
						type="button"
						variant={panelType === type ? 'default' : 'outline'}
						size="sm"
						onclick={() => selectType(type)}
					>
						{panelTypeLabel(type)}
					</Button>
				{/each}
			</div>

			{#if panelType === 'Logs'}
				<AddPanelLogsForm bind:this={logsForm} />
			{:else if panelType === 'Traces'}
				<AddPanelTracesForm bind:this={tracesForm} />
			{:else if panelType === 'Metrics'}
				<AddPanelMetricsForm bind:this={metricsForm} bind:valid={metricsValid} />
			{/if}

			{#if panelType}
				<div class="space-y-2">
					<label for="add-panel-title" class="text-sm font-medium">{m.addPanelDialog_titleLabel()}</label>
					<Input id="add-panel-title" bind:value={title} required />
				</div>
			{/if}

			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => handleOpenChange(false)}>{m.addPanelDialog_cancel()}</Button>
				<Button type="submit" disabled={!canSubmit}>{m.addPanelDialog_add()}</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
