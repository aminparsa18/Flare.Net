<script lang="ts">
	// Explorer toolbar action: "Create alert from this query". Builds the same
	// `AlertPanelDraft` DashboardPanelCard does and hops to /alerts with it as a deep link
	// (the alerts page opens AlertRuleFormDialog prefilled), so the drafting path is shared.
	// Hidden for a Viewer, same as every other mutating control - /alerts would 403 on submit.
	import { goto } from '$app/navigation';
	import { Button } from '$lib/components/ui/button';
	import { authContext } from '$lib/auth/context';
	import { buildAlertDeepLinkHref, type AlertPanelDraft } from '$lib/deep-links';
	import BellPlusIcon from '@lucide/svelte/icons/bell-plus';
	import * as m from '$lib/paraglide/messages';

	let { draft }: { draft: () => AlertPanelDraft | null } = $props();

	const auth = authContext.get();
</script>

{#if auth.canMutate}
	<Button
		variant="outline"
		size="sm"
		onclick={() => {
			const d = draft();
			if (d) void goto(buildAlertDeepLinkHref(d));
		}}
	>
		<BellPlusIcon data-icon="inline-start" />
		{m.createAlertButton_trigger()}
	</Button>
{/if}
