<script lang="ts">
	// First real use of the Card component (Table/Card's own "first real use" was the
	// Alerts page - this is Card's).
	//
	// Feedback: "Current arrivals"/"Current ingestion" read as near-synonyms even though
	// they're different concepts (requests vs. events vs. bytes), and "Current ingestion"
	// smuggled two units (events/minute *and* KB/minute) into one card's content line.
	// Split into five single-concept tiles instead of four multi-concept ones - the three
	// "right now" rates (ingress/event/data) each carry their unit right in the big number
	// rather than a separate description line, and the two window totals (requests/
	// rejected) stay as bare counts, since "Window requests"/"Rejected" already say what
	// they are without needing a unit suffix.
	import * as Card from '$lib/components/ui/card';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatBytes, formatCount } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';

	const ingestion = ingestionContext.get();

	const totals = $derived(ingestion.stats?.totals);
</script>

<div class="grid grid-cols-2 gap-3 p-4 sm:grid-cols-3 lg:grid-cols-5">
	<Card.Root>
		<Card.Header>
			<Card.Description>{m.ingestionTiles_ingressRate()}</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums">
				{formatCount(totals?.arrivalsPerMinute ?? 0)}
				<span class="text-muted-foreground text-xs font-normal">{m.ingestionTiles_reqPerMinUnit()}</span>
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.ingestionTiles_allSignals()}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.ingestionTiles_eventRate()}</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums">
				{formatCount(totals?.ingestedRecordsPerMinute ?? 0)}
				<span class="text-muted-foreground text-xs font-normal">{m.ingestionTiles_eventsPerMinUnit()}</span>
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.ingestionTiles_allSignals()}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.ingestionTiles_dataRate()}</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums">
				{formatBytes(totals?.ingestedBytesPerMinute ?? 0)}<span class="text-muted-foreground text-xs font-normal"
					>{m.ingestionTiles_perMinUnit()}</span
				>
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.ingestionTiles_allSignals()}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.ingestionTiles_windowRequests()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals?.requestsInWindow ?? 0)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.ingestionTiles_acceptedExportRequests()}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.ingestionTiles_rejected()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums {totals && totals.rejectedInWindow > 0 ? 'text-destructive' : ''}">
				{formatCount(totals?.rejectedInWindow ?? 0)}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.ingestionTiles_inSelectedWindow()}</Card.Content>
	</Card.Root>
</div>