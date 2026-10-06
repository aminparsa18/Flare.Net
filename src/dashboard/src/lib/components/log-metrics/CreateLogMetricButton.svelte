<script lang="ts">
	// Logs explorer toolbar action: "Create metric from this search". Hops to
	// /settings/log-metrics with the current filter as a `?filter=` draft (the page opens
	// LogMetricFormDialog prefilled) - same hand-off CreateAlertButton uses for /alerts.
	// Hidden for a Viewer, same as every other mutating control.
	import { goto } from '$app/navigation';
	import { Button } from '$lib/components/ui/button';
	import { authContext } from '$lib/auth/context';
	import { buildLogMetricDraftHref } from '$lib/log-metrics-api';
	import { stripIgnored } from '$lib/log-metrics/format';
	import { withBase } from '$lib/paths';
	import type { LogFilter } from '$lib/api';
	import SigmaIcon from '@lucide/svelte/icons/sigma';
	import * as m from '$lib/paraglide/messages';

	let { filter }: { filter: () => LogFilter } = $props();

	const auth = authContext.get();
</script>

{#if auth.canMutate}
	<Button variant="outline" size="sm" title={m.createLogMetricButton_hint()} onclick={() => void goto(buildLogMetricDraftHref(stripIgnored(filter()), withBase))}>
		<SigmaIcon data-icon="inline-start" />
		{m.createLogMetricButton_trigger()}
	</Button>
{/if}
