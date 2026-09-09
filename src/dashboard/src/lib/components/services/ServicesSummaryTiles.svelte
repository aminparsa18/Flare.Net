<script lang="ts">
	// Four at-a-glance headline numbers above the per-service table - same "what should I
	// care about" framing as IndexingSummaryTiles, picked to answer "is anything on fire
	// right now" before scanning the table row by row: how many services are even sending
	// traffic, the fleet-wide request rate, the fleet-wide error rate (request-weighted,
	// not a plain average of each service's own rate - see totals below), and whichever
	// single service has the worst p99 tail latency.
	import * as Card from '$lib/components/ui/card';
	import { servicesContext } from '$lib/services/context';
	import { formatMs, formatPercent } from '$lib/indexing/format';
	import { formatRequestRate } from '$lib/services/format';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.get();

	const totals = $derived.by(() => {
		const rows = services.services ?? [];
		const totalRequests = rows.reduce((sum, s) => sum + s.requestCount, 0);
		const totalErrors = rows.reduce((sum, s) => sum + s.errorCount, 0);
		return {
			serviceCount: rows.length,
			totalRequestsPerSecond: rows.reduce((sum, s) => sum + s.requestsPerSecond, 0),
			// Request-weighted (totalErrors / totalRequests), not the average of each row's
			// own errorRate - a service with 10k requests and a 1% error rate should move
			// this number far more than one with 10 requests and a 50% error rate.
			overallErrorRate: totalRequests === 0 ? null : totalErrors / totalRequests
		};
	});

	const slowestService = $derived.by(() => {
		const rows = services.services ?? [];
		if (rows.length === 0) return null;
		return rows.reduce((worst, s) => (s.p99DurationMs > worst.p99DurationMs ? s : worst), rows[0]);
	});

	function errorRateClass(errorRate: number | null): string {
		if (errorRate === null) return '';
		if (errorRate >= 0.05) return 'text-destructive';
		if (errorRate >= 0.01) return 'text-warning';
		return '';
	}
</script>

<div class="grid grid-cols-2 gap-3 p-4 lg:grid-cols-4">
	<Card.Root>
		<Card.Header>
			<Card.Description>{m.servicesSummaryTiles_services()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{totals.serviceCount}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.servicesSummaryTiles_sendingTraces()}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.servicesSummaryTiles_requestRate()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums">
				{m.servicesTable_requestRateValue({ rate: formatRequestRate(totals.totalRequestsPerSecond) })}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.servicesSummaryTiles_acrossServices({ count: totals.serviceCount })}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.servicesSummaryTiles_errorRate()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums {errorRateClass(totals.overallErrorRate)}">
				{totals.overallErrorRate === null ? '—' : formatPercent(totals.overallErrorRate * 100)}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.servicesSummaryTiles_requestWeighted()}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.servicesSummaryTiles_slowestService()}</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums">
				{#if slowestService}
					{formatMs(slowestService.p99DurationMs)}<span class="text-muted-foreground text-xs font-normal">p99</span>
				{:else}
					—
				{/if}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground truncate text-xs">
			{slowestService ? slowestService.serviceName : m.servicesSummaryTiles_noData()}
		</Card.Content>
	</Card.Root>
</div>
