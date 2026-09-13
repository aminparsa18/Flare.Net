<script lang="ts">
	// Toolbar trigger for PinToDashboardDialog.svelte - a thin wrapper so
	// LogsToolbar/MetricsToolbar/TracesToolbar each add one line rather than owning the
	// dialog's open state themselves.
	//
	// Pinning always mutates a dashboard (creates one, or PUTs a new panel onto an
	// existing one) - hidden entirely for a Viewer via `auth.canMutate`, same as every
	// other dashboard-mutating control (DashboardTable.svelte/[id]/+page.svelte), so a
	// Viewer never opens this dialog just to have it 403 on submit.
	import { Button } from '$lib/components/ui/button';
	import { authContext } from '$lib/auth/context';
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

	const auth = authContext.get();
	let open = $state(false);
</script>

{#if auth.canMutate}
	<Button variant="outline" size="sm" onclick={() => (open = true)}>
		<LayoutDashboardIcon data-icon="inline-start" />
		{m.pinToDashboard_trigger()}
	</Button>
	<PinToDashboardDialog bind:open {panelType} {currentState} {defaultTitle} />
{/if}
