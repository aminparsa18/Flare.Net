<script lang="ts">
	// Log-based metrics (ADR-0140): management page. Also the landing for the Logs explorer's
	// "Create metric" action, which arrives as `?new=1&filter=<LogFilter JSON>`.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { logMetricsContext } from '$lib/log-metrics/context';
	import { LogMetricsState } from '$lib/log-metrics/state.svelte';
	import { parseLogMetricDraft } from '$lib/log-metrics-api';
	import LogMetricTable from '$lib/components/log-metrics/LogMetricTable.svelte';
	import LogMetricFormDialog from '$lib/components/log-metrics/LogMetricFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const logMetrics = logMetricsContext.set(new LogMetricsState());

	onMount(() => {
		void logMetrics.load();
		const draft = parseLogMetricDraft(page.url);
		if (draft) logMetrics.openCreate(draft);
	});
</script>

<svelte:head>
	<title>{m.logMetricsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<LogMetricTable />
</div>
<LogMetricFormDialog />
