<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Separator } from '$lib/components/ui/separator';
	import AttributeTable from '$lib/components/logs/AttributeTable.svelte';
	import { statusVariant, statusLabel, kindLabel } from '$lib/traces/status';
	import { formatDurationNano } from '$lib/traces/duration';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import { searchLogs, type LogEventDto } from '$lib/api';
	import { severityVariant } from '$lib/logs/severity';

	const detail = traceDetailContext.get();

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	// Linked logs: view-local, transient data for whichever span is currently selected -
	// no other consumer needs it, so it doesn't belong on TraceDetailState. Re-fetched
	// (not accumulated) every time the selected span changes.
	let linkedLogs = $state<LogEventDto[]>([]);
	let linkedLogsLoading = $state(false);
	let linkedLogsAbort: AbortController | null = null;

	$effect(() => {
		const span = detail.selectedSpan;
		linkedLogsAbort?.abort();

		if (!span) {
			linkedLogs = [];
			linkedLogsLoading = false;
			return;
		}

		const abort = new AbortController();
		linkedLogsAbort = abort;
		linkedLogsLoading = true;

		searchLogs({ filter: { traceId: span.traceId, spanId: span.spanId }, pageSize: 20 }, abort.signal)
			.then((result) => {
				if (abort.signal.aborted) return;
				linkedLogs = result.events;
			})
			.catch((err) => {
				if (abort.signal.aborted) return;
				console.error('Failed to load linked logs', err);
				linkedLogs = [];
			})
			.finally(() => {
				if (!abort.signal.aborted) linkedLogsLoading = false;
			});

		return () => abort.abort();
	});
</script>

<Sheet.Root
	open={detail.selectedSpan !== null}
	onOpenChange={(next) => {
		if (!next) detail.selectedSpanId = null;
	}}
>
	<!-- Same wide-sheet rationale as EventDetailSheet - attribute values (URLs, SQL
	     statements, stack traces on error spans) mostly fit on one line at this width. -->
	<Sheet.Content class="flex w-full flex-col sm:max-w-5xl">
		{#if detail.selectedSpan}
			{@const span = detail.selectedSpan}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					<Badge variant={statusVariant(span.statusCode)}>{statusLabel(span.statusCode)}</Badge>
					{span.name || '—'}
					<span class="text-muted-foreground font-normal">· {span.serviceName || '—'}</span>
				</Sheet.Title>
				<Sheet.Description>
					{formatTimestamp(span.startTime)} · {kindLabel(span.kind)} · {formatDurationNano(span.durationNano)}
				</Sheet.Description>
			</Sheet.Header>
			<ScrollArea class="min-h-0 flex-1 px-4">
				<div class="flex flex-col gap-4 pb-8">
					{#if span.statusMessage}
						<div class="border-destructive/50 bg-destructive/5 rounded-md border p-3">
							<p class="text-destructive text-sm break-words">{span.statusMessage}</p>
						</div>
					{/if}

					<div class="grid grid-cols-2 gap-2 text-xs">
						<div>
							<span class="text-muted-foreground">Trace ID</span>
							<p class="truncate font-mono">{span.traceId}</p>
						</div>
						<div>
							<span class="text-muted-foreground">Span ID</span>
							<p class="truncate font-mono">{span.spanId}</p>
						</div>
						<div>
							<span class="text-muted-foreground">Parent span ID</span>
							<p class="truncate font-mono">{span.parentSpanId || '— (root span)'}</p>
						</div>
						<div>
							<span class="text-muted-foreground">Trace state</span>
							<p class="truncate font-mono">{span.traceState || '—'}</p>
						</div>
					</div>

					<Separator />

					<AttributeTable title="Span attributes" attributes={span.spanAttributes} />
					<AttributeTable title="Resource attributes" attributes={span.resourceAttributes} />
					<AttributeTable title="Scope attributes" attributes={span.scopeAttributes} />

					{#if span.events.length > 0}
						<div>
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">Events</h3>
							<div class="flex flex-col gap-2">
								{#each span.events as event, i (i)}
									<div class="rounded-md border p-2">
										<div class="flex items-baseline justify-between gap-2">
											<span class="text-sm font-medium">{event.name || '—'}</span>
											<span class="text-muted-foreground shrink-0 font-mono text-xs">{formatTimestamp(event.timestamp)}</span>
										</div>
										<AttributeTable title="Attributes" attributes={event.attributes} />
									</div>
								{/each}
							</div>
						</div>
					{/if}

					{#if linkedLogs.length > 0}
						<div>
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">Linked logs</h3>
							<div class="flex flex-col gap-2">
								{#each linkedLogs as log (log.eventId)}
									<div class="rounded-md border p-2">
										<div class="flex items-baseline justify-between gap-2">
											<Badge variant={severityVariant(log.severityNumber)}>{log.severityText || '—'}</Badge>
											<span class="text-muted-foreground shrink-0 font-mono text-xs">{formatTimestamp(log.timestamp)}</span>
										</div>
										<p class="mt-1 truncate text-sm">{log.body}</p>
									</div>
								{/each}
							</div>
						</div>
					{/if}
				</div>
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
