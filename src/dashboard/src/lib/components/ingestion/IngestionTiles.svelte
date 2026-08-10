<script lang="ts">
	// First real use of the Card component (Table/Card's own "first real use" was the
	// Alerts page - this is Card's).
	import * as Card from '$lib/components/ui/card';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatBytes, formatCount } from '$lib/ingestion/format';

	const ingestion = ingestionContext.get();

	const totals = $derived(ingestion.stats?.totals);
</script>

<div class="grid grid-cols-2 gap-3 p-4 lg:grid-cols-4">
	<Card.Root>
		<Card.Header>
			<Card.Description>Current arrivals</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals?.arrivalsPerMinute ?? 0)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">requests/minute, all signals</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Current ingestion</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals?.ingestedRecordsPerMinute ?? 0)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			events/minute · {formatBytes(totals?.ingestedBytesPerMinute ?? 0)}/minute
		</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Requests in window</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals?.requestsInWindow ?? 0)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">accepted export requests</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Rejected payloads</Card.Description>
			<Card.Title class="text-2xl tabular-nums {totals && totals.rejectedInWindow > 0 ? 'text-destructive' : ''}">
				{formatCount(totals?.rejectedInWindow ?? 0)}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">in the selected window</Card.Content>
	</Card.Root>
</div>
