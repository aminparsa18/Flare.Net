<script lang="ts">
	// One panel's card chrome (title, type badge, remove) around whichever
	// panels/Dashboard*PanelBody.svelte matches its `panelType` - fixed single-column
	// stack for now (no drag/resize yet, see docs-internal/adr/0023-custom-dashboards.md's
	// Phase 2+ scope).
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import DashboardLogsPanelBody from './panels/DashboardLogsPanelBody.svelte';
	import DashboardMetricsPanelBody from './panels/DashboardMetricsPanelBody.svelte';
	import DashboardTracesPanelBody from './panels/DashboardTracesPanelBody.svelte';
	import type { DashboardPanel } from '$lib/dashboards-api';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	let {
		panel,
		removing,
		onRemove
	}: {
		panel: DashboardPanel;
		removing: boolean;
		onRemove: () => void;
	} = $props();

	function panelTypeLabel(panelType: DashboardPanel['panelType']): string {
		switch (panelType) {
			case 'Logs':
				return m.nav_logs();
			case 'Traces':
				return m.nav_traces();
			case 'Metrics':
				return m.nav_metrics();
		}
	}

	function handleRemove(): void {
		if (!confirm(m.dashboardViewer_confirmRemovePanel({ title: panel.title }))) return;
		onRemove();
	}
</script>

<div class="flex flex-col rounded-lg border">
	<div class="flex items-center justify-between gap-2 border-b px-3 py-2">
		<div class="flex min-w-0 items-center gap-2">
			<span class="truncate text-sm font-medium">{panel.title}</span>
			<Badge variant="outline">{panelTypeLabel(panel.panelType)}</Badge>
		</div>
		<Button
			variant="ghost"
			size="icon-sm"
			class="text-muted-foreground hover:text-destructive shrink-0"
			title={m.dashboardViewer_removePanel()}
			disabled={removing}
			onclick={handleRemove}
		>
			{#if removing}<Spinner class="size-3" />{:else}<Trash2Icon />{/if}
		</Button>
	</div>
	<div class="flex h-80 min-h-0 flex-col overflow-hidden">
		{#if panel.panelType === 'Logs'}
			<DashboardLogsPanelBody query={panel.query} />
		{:else if panel.panelType === 'Metrics'}
			<DashboardMetricsPanelBody query={panel.query} />
		{:else if panel.panelType === 'Traces'}
			<DashboardTracesPanelBody query={panel.query} />
		{/if}
	</div>
</div>
