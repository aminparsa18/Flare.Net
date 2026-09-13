<script lang="ts">
	// "Pin to dashboard" - how a panel actually gets created in v1 (see
	// docs-internal/adr/0023-custom-dashboards.md's Phase 1 scope): takes the calling
	// Explorer page's current filter state verbatim (same `currentState()` shape
	// SaveSearchDialog/SaveViewDialog already use to create a SavedView) and appends it
	// as a new panel on an existing or brand-new dashboard. Generic over `panelType`, used
	// from LogsToolbar/MetricsToolbar/TracesToolbar alike.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import { listDashboards, createDashboard, updateDashboard, type DashboardSummary, type PanelType } from '$lib/dashboards-api';
	import { nextPanelPosition } from '$lib/dashboards/layout';
	import * as m from '$lib/paraglide/messages';

	let {
		open = $bindable(false),
		panelType,
		currentState,
		defaultTitle
	}: {
		open: boolean;
		panelType: PanelType;
		/** Called at submit time (not eagerly) - same "reflect whatever's current when the user actually clicks" reasoning SaveSearchDialog.svelte's own `currentState` prop documents. */
		currentState: () => unknown;
		defaultTitle: string;
	} = $props();

	const NEW_DASHBOARD = '__new__';

	let dashboards = $state<DashboardSummary[]>([]);
	let loadingDashboards = $state(false);
	let selectedId = $state<string>(NEW_DASHBOARD);
	let newDashboardName = $state('');
	let panelTitle = $state('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	async function loadDashboards(): Promise<void> {
		loadingDashboards = true;
		try {
			dashboards = (await listDashboards()).dashboards;
			selectedId = dashboards.length > 0 ? dashboards[0].id : NEW_DASHBOARD;
		} catch {
			dashboards = []; // non-critical - falls back to "new dashboard" only
			selectedId = NEW_DASHBOARD;
		} finally {
			loadingDashboards = false;
		}
	}

	$effect(() => {
		if (open) {
			panelTitle = defaultTitle;
			newDashboardName = '';
			error = null;
			void loadDashboards();
		}
	});

	function handleOpenChange(next: boolean): void {
		if (!saving) open = next;
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		saving = true;
		error = null;
		try {
			const target = selectedId === NEW_DASHBOARD ? null : dashboards.find((d) => d.id === selectedId);
			if (selectedId !== NEW_DASHBOARD && !target) throw new Error('Selected dashboard no longer exists.');
			const existingPanels = target?.layout.panels ?? [];
			const panel = {
				id: crypto.randomUUID(),
				panelType,
				title: panelTitle,
				layout: nextPanelPosition(existingPanels),
				query: currentState()
			};

			if (!target) {
				await createDashboard({ name: newDashboardName, description: '', layout: { panels: [panel] } });
			} else {
				await updateDashboard(target.id, {
					name: target.name,
					description: target.description,
					layout: { panels: [...existingPanels, panel] }
				});
			}
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}

	const selectedLabel = $derived(
		selectedId === NEW_DASHBOARD ? m.pinToDashboardDialog_newDashboardOption() : (dashboards.find((d) => d.id === selectedId)?.name ?? '')
	);
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.pinToDashboardDialog_title()}</Dialog.Title>
			<Dialog.Description>{m.pinToDashboardDialog_description()}</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-2">
				<span class="text-sm font-medium">{m.pinToDashboardDialog_dashboardLabel()}</span>
				{#if loadingDashboards}
					<div class="text-muted-foreground flex items-center gap-2 text-xs"><Spinner class="size-3" /> …</div>
				{:else}
					<Select.Root type="single" value={selectedId} onValueChange={(v) => v && (selectedId = v)}>
						<Select.Trigger class="w-full">{selectedLabel}</Select.Trigger>
						<Select.Content>
							<Select.Item value={NEW_DASHBOARD} label={m.pinToDashboardDialog_newDashboardOption()} />
							{#each dashboards as dashboard (dashboard.id)}
								<Select.Item value={dashboard.id} label={dashboard.name} />
							{/each}
						</Select.Content>
					</Select.Root>
				{/if}
				{#if !loadingDashboards && dashboards.length === 0}
					<p class="text-muted-foreground text-xs">{m.pinToDashboardDialog_noDashboards()}</p>
				{/if}
			</div>
			{#if selectedId === NEW_DASHBOARD}
				<div class="space-y-2">
					<label for="pin-new-dashboard-name" class="text-sm font-medium">{m.dashboardFormDialog_nameLabel()}</label>
					<Input id="pin-new-dashboard-name" bind:value={newDashboardName} required />
				</div>
			{/if}
			<div class="space-y-2">
				<label for="pin-panel-title" class="text-sm font-medium">{m.pinToDashboardDialog_titleLabel()}</label>
				<Input id="pin-panel-title" bind:value={panelTitle} required />
			</div>
			{#if error}
				<p class="text-destructive text-sm">{error}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => (open = false)}>{m.pinToDashboardDialog_cancel()}</Button>
				<Button type="submit" disabled={saving}>
					{#if saving}<Spinner class="size-4" />{/if}
					{m.pinToDashboardDialog_pin()}
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
