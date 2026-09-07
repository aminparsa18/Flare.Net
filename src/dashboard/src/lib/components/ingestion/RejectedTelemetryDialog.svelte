<script lang="ts">
	// The drill-down behind IngestionSignalsTable's Rejected count (Planning.md v10
	// follow-up feedback: "40 rejected" by itself gives no way to investigate). Scoped to
	// one (signal, protocol) - the same grain as the row that opened it - and built entirely
	// from data already on the page (stats.recentErrors), no new endpoint needed: each
	// rejection is recorded with a reason string at the point Flare.Ingest's OTLP receiver
	// gives up on it (RecordRejectedAsync), so this is a client-side group-by/first-last-seen
	// over that same list IngestionLog already renders unfiltered.
	//
	// Deliberately has no "Affected services" field, unlike the field this was scoped
	// against - a rejection this early (bad protobuf/JSON, wrong content-type) means the
	// export was never parsed far enough to read a resource's service.name from it. Showing
	// one would mean inventing data the receiver never had.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { ingestionContext } from '$lib/ingestion/context';
	import { INGESTION_WINDOW_PRESETS } from '$lib/ingestion/state.svelte';
	import { formatCount, protocolLabel, signalLabel } from '$lib/ingestion/format';
	import type { IngestionProtocol, IngestionSignal } from '$lib/ingestion-api';
	import * as m from '$lib/paraglide/messages';

	let {
		open = $bindable(false),
		signal,
		protocol,
		rejectedCount
	}: {
		open?: boolean;
		signal: IngestionSignal;
		protocol: IngestionProtocol;
		rejectedCount: number;
	} = $props();

	const ingestion = ingestionContext.get();

	// A function, not a static lookup object - see time-range.ts's own remarks on why a
	// module-scope const can't reflect a per-request/live-switched locale.
	function signalNoun(signal: IngestionSignal): string {
		switch (signal) {
			case 'Logs':
				return m.rejectedTelemetryDialog_logNoun();
			case 'Traces':
				return m.rejectedTelemetryDialog_traceNoun();
			case 'Metrics':
				return m.rejectedTelemetryDialog_metricNoun();
		}
	}

	// recentErrors has no window param of its own (it's a flat last-200 FIFO, see
	// IngestionStatsKeys.MaxErrorEntries) - filtering by the page's selected window here
	// keeps this dialog's numbers from silently disagreeing with the rejectedCount the
	// reader just clicked, which *is* windowed (summed from the per-minute buckets).
	const windowMinutes = $derived(INGESTION_WINDOW_PRESETS.find((p) => p.value === ingestion.windowPreset)?.minutes ?? 60);

	const sample = $derived.by(() => {
		const cutoff = Date.now() - windowMinutes * 60_000;
		return (ingestion.stats?.recentErrors ?? []).filter(
			(e) => e.signal === signal && e.protocol === protocol && new Date(e.timestamp).getTime() >= cutoff
		);
	});

	// recentErrors is LPUSH-newest-first server-side (RedisIngestionStatsTracker), and
	// Array.filter preserves that order - so index 0 is the most recent match, the last
	// index the oldest one still retained.
	const lastSeen = $derived(sample.length > 0 ? sample[0].timestamp : null);
	const firstSeen = $derived(sample.length > 0 ? sample[sample.length - 1].timestamp : null);

	const reasonCounts = $derived.by(() => {
		const counts = new Map<string, number>();
		for (const entry of sample) {
			counts.set(entry.reason, (counts.get(entry.reason) ?? 0) + 1);
		}
		return [...counts.entries()].map(([reason, count]) => ({ reason, count })).sort((a, b) => b.count - a.count);
	});

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	function viewLog(): void {
		ingestion.setLogFilter(signal, protocol);
		open = false;
		document.getElementById('ingestion-log')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
	}
</script>

<Dialog.Root {open} onOpenChange={(v) => (open = v)}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.rejectedTelemetryDialog_title()}</Dialog.Title>
			<Dialog.Description>
				{rejectedCount === 1
					? m.rejectedTelemetryDialog_descriptionSingular({ count: formatCount(rejectedCount), noun: signalNoun(signal) })
					: m.rejectedTelemetryDialog_descriptionPlural({ count: formatCount(rejectedCount), noun: signalNoun(signal) })}
			</Dialog.Description>
		</Dialog.Header>

		<div class="space-y-4 text-sm">
			<div>
				<div class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{m.rejectedTelemetryDialog_reasonLabel()}</div>
				{#if reasonCounts.length === 0}
					<p class="text-muted-foreground text-xs">{m.rejectedTelemetryDialog_noEntriesLeft()}</p>
				{:else}
					<div class="space-y-0.5">
						{#each reasonCounts as r (r.reason)}
							<div class="font-mono text-xs">{formatCount(r.count)} × {r.reason}</div>
						{/each}
					</div>
				{/if}
			</div>

			<div class="grid grid-cols-2 gap-3">
				<div>
					<div class="text-muted-foreground text-xs font-medium tracking-wide uppercase">{m.rejectedTelemetryDialog_protocolLabel()}</div>
					<div>{protocolLabel(protocol)}</div>
				</div>
				<div>
					<div class="text-muted-foreground text-xs font-medium tracking-wide uppercase">{m.rejectedTelemetryDialog_signalLabel()}</div>
					<div>{signalLabel(signal)}</div>
				</div>
				<div>
					<div class="text-muted-foreground text-xs font-medium tracking-wide uppercase">{m.rejectedTelemetryDialog_firstSeenLabel()}</div>
					<div class="tabular-nums">{firstSeen ? formatTime(firstSeen) : '—'}</div>
				</div>
				<div>
					<div class="text-muted-foreground text-xs font-medium tracking-wide uppercase">{m.rejectedTelemetryDialog_lastSeenLabel()}</div>
					<div class="tabular-nums">{lastSeen ? formatTime(lastSeen) : '—'}</div>
				</div>
			</div>

			{#if sample.length > 0 && sample.length < rejectedCount}
				<p class="text-muted-foreground text-xs">
					{m.rejectedTelemetryDialog_showingMostRecent({ shown: formatCount(sample.length), total: formatCount(rejectedCount) })}
				</p>
			{/if}

			<p class="text-muted-foreground text-xs">
				{m.rejectedTelemetryDialog_noAffectedServices()}
			</p>
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={viewLog}>{m.rejectedTelemetryDialog_viewRejectedPayloads()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>