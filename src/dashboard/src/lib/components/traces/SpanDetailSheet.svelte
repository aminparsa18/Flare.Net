<script lang="ts">
	import { goto } from '$app/navigation';
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Separator } from '$lib/components/ui/separator';
	import { Button } from '$lib/components/ui/button';
	import AttributeTable from '$lib/components/logs/AttributeTable.svelte';
	import StackTraceViewer from '$lib/components/logs/StackTraceViewer.svelte';
	import { statusVariant, statusLabel, kindLabel } from '$lib/traces/status';
	import { formatDurationNano } from '$lib/traces/duration';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import { searchLogs, type LogEventDto } from '$lib/api';
	import { severityVariant } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';

	const detail = traceDetailContext.get();

	// Same OTel exception semantic-conventions key EventDetailSheet special-cases out of the
	// generic attribute table - here it matters even more, since AttributeTable truncates
	// every value to one line and a stack trace is the one attribute value that's never
	// meaningfully readable truncated to one line.
	const EXCEPTION_STACKTRACE_KEY = 'exception.stacktrace';

	function eventAttributesWithoutStacktrace(attributes: Record<string, string>): Record<string, string> {
		if (!(EXCEPTION_STACKTRACE_KEY in attributes)) return attributes;
		const rest = { ...attributes };
		delete rest[EXCEPTION_STACKTRACE_KEY];
		return rest;
	}

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
							<span class="text-muted-foreground">{m.spanDetail_traceId()}</span>
							<p class="truncate font-mono">{span.traceId}</p>
						</div>
						<div>
							<span class="text-muted-foreground">{m.spanDetail_spanId()}</span>
							<p class="truncate font-mono">{span.spanId}</p>
						</div>
						<div>
							<span class="text-muted-foreground">{m.spanDetail_parentSpanId()}</span>
							<p class="truncate font-mono">{span.parentSpanId || m.spanDetail_rootSpanFallback()}</p>
						</div>
						<div>
							<span class="text-muted-foreground">{m.spanDetail_traceState()}</span>
							<p class="truncate font-mono">{span.traceState || '—'}</p>
						</div>
					</div>

					<Separator />

					<AttributeTable title={m.spanDetail_spanAttributesTitle()} attributes={span.spanAttributes} />
					<AttributeTable title={m.spanDetail_resourceAttributesTitle()} attributes={span.resourceAttributes} />
					<AttributeTable title={m.spanDetail_scopeAttributesTitle()} attributes={span.scopeAttributes} />

					{#if span.events.length > 0}
						<div>
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{m.spanDetail_eventsTitle()}</h3>
							<div class="flex flex-col gap-2">
								{#each span.events as event, i (i)}
									<div class="rounded-md border p-2">
										<div class="flex items-baseline justify-between gap-2">
											<span class="text-sm font-medium">{event.name || '—'}</span>
											<span class="text-muted-foreground shrink-0 font-mono text-xs">{formatTimestamp(event.timestamp)}</span>
										</div>
										{#if event.attributes[EXCEPTION_STACKTRACE_KEY]}
											<div class="mt-1 mb-2">
												<h4 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">
													{m.spanDetail_stackTraceTitle()}
												</h4>
												<StackTraceViewer trace={event.attributes[EXCEPTION_STACKTRACE_KEY]} maxHeight="16rem" />
											</div>
										{/if}
										<AttributeTable title={m.spanDetail_attributesTitle()} attributes={eventAttributesWithoutStacktrace(event.attributes)} />
									</div>
								{/each}
							</div>
						</div>
					{/if}

					{#if span.links.length > 0}
						<div>
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{m.spanDetail_linksTitle()}</h3>
							<div class="flex flex-col gap-2">
								{#each span.links as link, i (i)}
									<div class="rounded-md border p-2">
										<div class="flex items-baseline justify-between gap-2">
											<span class="truncate font-mono text-xs">
												{link.traceId} / {link.spanId}
											</span>
											<Button variant="ghost" size="xs" class="shrink-0" onclick={() => goto(`/traces/${link.traceId}`)}>
												{m.spanDetail_viewLinkedTrace()}
											</Button>
										</div>
										{#if link.traceState}
											<p class="text-muted-foreground mt-1 truncate font-mono text-xs">{link.traceState}</p>
										{/if}
										<AttributeTable title={m.spanDetail_attributesTitle()} attributes={link.attributes} />
									</div>
								{/each}
							</div>
						</div>
					{/if}

					{#if linkedLogs.length > 0}
						<div>
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{m.spanDetail_linkedLogsTitle()}</h3>
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
