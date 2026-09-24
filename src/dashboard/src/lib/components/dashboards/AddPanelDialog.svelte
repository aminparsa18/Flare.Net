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
	import { untrack } from 'svelte';
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

	function activeFormState(): unknown {
		return panelType === 'Logs'
			? logsForm?.currentState()
			: panelType === 'Traces'
				? tracesForm?.currentState()
				: metricsForm?.currentState();
	}

	// Discard-confirmation baseline (signoz#4188): the freshly-mounted form's own
	// serialized query, captured once per mount - untracked, so later filter edits don't
	// move the baseline along with them. Every form's currentState() is a plain
	// toSavedViewState() of user-driven filter fields only (nothing clock- or
	// fetch-derived), so "differs from its own starting value" is exactly "the user
	// changed something". Re-captured on a type switch, since the {#if} below mounts a
	// fresh form (and a fresh bind:this ref) for the new type.
	let baseline = $state<string | null>(null);
	$effect(() => {
		const form = panelType === 'Logs' ? logsForm : panelType === 'Traces' ? tracesForm : metricsForm;
		baseline = form ? untrack(() => JSON.stringify(activeFormState())) : null;
	});

	function isDirty(): boolean {
		if (panelType === null) return false;
		if (title.trim() !== panelTypeLabel(panelType)) return true;
		return baseline !== null && JSON.stringify(activeFormState()) !== baseline;
	}

	function reset(): void {
		panelType = null;
		title = '';
		metricsValid = false;
	}

	function close(): void {
		open = false;
		reset();
	}

	// Every dismissal path (Cancel, X, Escape, overlay click) funnels through here via the
	// function binding on Dialog.Root below - declining the prompt just never writes
	// `false`, so the getter keeps reporting open and bits-ui stays open with it. Native
	// confirm(), same as every other destructive-action prompt in this dashboard (e.g.
	// AlertRuleTable's delete).
	function handleOpenChange(next: boolean): void {
		if (next) {
			open = true;
			return;
		}
		if (isDirty() && !confirm(m.addPanelDialog_discardConfirm())) return;
		close();
	}

	function handleSubmit(event: SubmitEvent): void {
		event.preventDefault();
		if (!panelType) return;
		onAdd({
			id: crypto.randomUUID(),
			panelType,
			title: title.trim(),
			layout: nextPanelPosition(existingPanels),
			query: activeFormState()
		});
		close(); // submitted, not discarded - no prompt
	}
</script>

<Dialog.Root bind:open={() => open, handleOpenChange}>
	<!-- sm:max-w-4xl (not the original 2xl) - AddPanelMetricsForm's Formula tab needs room
	     for FormulaBuilder's fixed w-[420px] query-row column alongside a usable chart
	     preview (docs-internal/adr/0037-dashboard-metrics-formula-panels.md); 4xl still
	     comfortably fits Logs/Traces' simpler forms and single-metric Metrics' 220px
	     MetricPicker + chart. -->
	<Dialog.Content class="sm:max-w-4xl">
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
