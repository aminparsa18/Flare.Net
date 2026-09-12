<script lang="ts">
	// Toolbar trigger for PinToDashboardDialog.svelte - a thin wrapper so
	// LogsToolbar/MetricsToolbar/TracesToolbar each add one line rather than owning the
	// dialog's open state themselves.
	import { Button } from '$lib/components/ui/button';
	import PinToDashboardDialog from './PinToDashboardDialog.svelte';
	import type { PanelType } from '$lib/dashboards-api';
	import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
	import * as m from '$lib/paraglide/messages';

	let {
		panelType,
		currentState,
		defaultTitle
	}: {
		panelType: PanelType;
		currentState: () => unknown;
		defaultTitle: string;
	} = $props();

	let open = $state(false);
</script>

<Button variant="outline" size="sm" onclick={() => (open = true)}>
	<LayoutDashboardIcon data-icon="inline-start" />
	{m.pinToDashboard_trigger()}
</Button>
<PinToDashboardDialog bind:open {panelType} {currentState} {defaultTitle} />
