<script lang="ts">
	import { withBase } from '$lib/paths';
	import AnsiText from '$lib/components/logs/AnsiText.svelte';
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Separator } from '$lib/components/ui/separator';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { AROUND_DEFAULT_MS } from '$lib/time/around';
	import { Button } from '$lib/components/ui/button';
	import AttributeTable from '$lib/components/logs/AttributeTable.svelte';
	import StackTraceViewer from '$lib/components/logs/StackTraceViewer.svelte';
	import EventHostMetrics from '$lib/components/logs/EventHostMetrics.svelte';
	import EventPodMetrics from '$lib/components/logs/EventPodMetrics.svelte';
	import { statusVariant, statusLabel, kindLabel } from '$lib/traces/status';
	import { formatDurationNano } from '$lib/traces/duration';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import { searchLogs, type LogEventDto } from '$lib/api';
	import { getSpanDurationPercentile, type SpanAttributeBag, type SpanDto, type SpanDurationPercentile } from '$lib/traces-api';
	import { buildSpanLogsHref, buildSpanNameTracesHref, buildTracesAroundHref, buildTracesAttributeFilterHref } from '$lib/deep-links';
	import { goto } from '$app/navigation';
	import { pinnedSpanAttributes } from '$lib/logs/pinned-attributes.svelte';
	import { severityVariant } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';
	import { formatTimestamp } from '$lib/time/format';
	import LinkIcon from '@lucide/svelte/icons/link';
	import CheckIcon from '@lucide/svelte/icons/check';

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

	// Linked logs: view-local, transient data for whichever span is currently selected -
	// no other consumer needs it, so it doesn't belong on TraceDetailState. Re-fetched
	// (not accumulated) every time the selected span changes.
	// Without an explicit From/To the backend applies its 1h-before-now DefaultLookback, so
	// logs of any older span would come back empty. Padded because logs are often flushed
	// slightly after the span ends (same +-5m as the CLI incident bundle's --margin default).
	const LINKED_LOGS_MARGIN_MS = 5 * 60_000;
	function linkedLogsWindow(span: SpanDto): { from: string; to: string } {
		return {
			from: new Date(new Date(span.startTime).getTime() - LINKED_LOGS_MARGIN_MS).toISOString(),
			to: new Date(new Date(span.endTime).getTime() + LINKED_LOGS_MARGIN_MS).toISOString()
		};
	}
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

		const window = linkedLogsWindow(span);
		searchLogs({ filter: { traceId: span.traceId, spanId: span.spanId, from: window.from, to: window.to }, pageSize: 20 }, abort.signal)
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

	// Duration percentile: same view-local, refetch-on-selection shape as linked logs. A
	// failed lookup just hides the line - it's context, not something the sheet depends on.
	// Below MIN_PERCENTILE_SAMPLES similar spans a rank means nothing, so none is shown.
	const MIN_PERCENTILE_SAMPLES = 10;
	let percentile = $state<SpanDurationPercentile | null>(null);

	$effect(() => {
		const span = detail.selectedSpan;
		percentile = null;
		if (!span || !span.name) return;

		const abort = new AbortController();
		getSpanDurationPercentile(span, abort.signal)
			.then((result) => {
				if (!abort.signal.aborted) percentile = result;
			})
			.catch((err) => {
				if (!abort.signal.aborted) console.error('Failed to load span duration percentile', err);
			});
		return () => abort.abort();
	});

	// Same host/pod resolution as EventDetailSheet: `host.name`, else the pod's node, plus
	// the pod's own kubeletstats charts - windowed around the span's start time.
	const metricsHost = $derived(detail.selectedSpan?.resourceAttributes['host.name'] || detail.selectedSpan?.resourceAttributes['k8s.node.name'] || null);
	const metricsPod = $derived.by(() => {
		const resource = detail.selectedSpan?.resourceAttributes;
		const name = resource?.['k8s.pod.name'];
		return name ? { name, namespace: resource?.['k8s.namespace.name'] || null } : null;
	});

	let linkCopied = $state(false);
	let linkCopiedResetTimer: ReturnType<typeof setTimeout> | undefined;

	// `?span=` is read back by the trace page (TraceDetailState.load) - selects the span,
	// expands its ancestors and scrolls it into view.
	async function copySpanLink(traceId: string, spanId: string): Promise<void> {
		const url = new URL(withBase(`/traces/${encodeURIComponent(traceId)}`), window.location.origin);
		url.searchParams.set('span', spanId);
		await navigator.clipboard.writeText(url.toString());
		linkCopied = true;
		clearTimeout(linkCopiedResetTimer);
		linkCopiedResetTimer = setTimeout(() => (linkCopied = false), 1500);
	}

	// Leaves the trace for the Traces list filtered to this value - see
	// buildTracesAttributeFilterHref for how a root vs. child span's attribute goes in.
	// Span event/link attributes get no filter: SpanFilter has no bag for them.
	function filterInto(span: SpanDto, bag: SpanAttributeBag) {
		return (key: string, value: string, exclude: boolean) => goto(buildTracesAttributeFilterHref(span, { bag, key, value }, exclude));
	}

	const pinProps = {
		isPinned: (key: string) => pinnedSpanAttributes.has(key),
		onTogglePin: (key: string) => pinnedSpanAttributes.toggle(key)
	};

	// Pinned keys pulled out of whichever bag holds them (span, then resource, then scope)
	// into one Pinned table, in pin order, and dropped from that bag's own table.
	function splitPinned(span: SpanDto) {
		const bags: [SpanAttributeBag, Record<string, string>][] = [
			['Span', { ...span.spanAttributes }],
			['Resource', { ...span.resourceAttributes }],
			['Scope', { ...span.scopeAttributes }]
		];
		const pinned: Record<string, string> = {};
		const pinnedBags = new Map<string, SpanAttributeBag>();
		for (const key of pinnedSpanAttributes.keys) {
			const found = bags.find(([, b]) => key in b);
			if (!found) continue;
			pinned[key] = found[1][key];
			pinnedBags.set(key, found[0]);
			delete found[1][key];
		}
		return { pinned, pinnedBags, span: bags[0][1], resource: bags[1][1], scope: bags[2][1] };
	}
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
			{@const canFilterOut = !span.parentSpanId}
			{@const sections = splitPinned(span)}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					<Badge variant={statusVariant(span.statusCode)}>{statusLabel(span.statusCode)}</Badge>
					{span.name || '—'}
					<span class="text-muted-foreground font-normal">· {span.serviceName || '—'}</span>
				</Sheet.Title>
				<Sheet.Description>
					{formatTimestamp(span.startTime)} · {kindLabel(span.kind)} · {formatDurationNano(span.durationNano)}
				</Sheet.Description>
				{#if percentile && percentile.sampleCount >= MIN_PERCENTILE_SAMPLES}
					<a
						href={buildSpanNameTracesHref(span)}
						class="text-muted-foreground hover:text-foreground w-fit text-xs underline-offset-2 hover:underline"
						title={m.spanDetail_percentileTitle({
							count: percentile.sampleCount,
							p50: formatDurationNano(percentile.p50Nano),
							p95: formatDurationNano(percentile.p95Nano),
							p99: formatDurationNano(percentile.p99Nano)
						})}
					>
						{m.spanDetail_percentileLabel({ percentile: Math.round(percentile.percentile), name: span.name, service: span.serviceName || '—' })}
					</a>
				{/if}
				<div class="flex flex-wrap gap-2">
					<Button variant="outline" size="sm" href={buildTracesAroundHref(new Date(span.startTime).getTime(), AROUND_DEFAULT_MS)}>
						<ClockIcon data-icon="inline-start" />
						{m.spanDetail_aroundThis()}
					</Button>
					<Button variant="outline" size="sm" onclick={() => copySpanLink(span.traceId, span.spanId)}>
						{#if linkCopied}
							<CheckIcon data-icon="inline-start" />
							{m.spanDetail_linkCopied()}
						{:else}
							<LinkIcon data-icon="inline-start" />
							{m.spanDetail_copyLink()}
						{/if}
					</Button>
				</div>
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

					{#if metricsPod}
						<EventPodMetrics podName={metricsPod.name} namespace={metricsPod.namespace} timestamp={span.startTime} />
					{/if}
					{#if metricsHost}
						<EventHostMetrics hostName={metricsHost} timestamp={span.startTime} />
					{/if}

					<Separator />

					<AttributeTable
						title={m.eventDetail_pinnedAttributes()}
						attributes={sections.pinned}
						{...pinProps}
						onFilter={(key, value, exclude) => filterInto(span, sections.pinnedBags.get(key) ?? 'Span')(key, value, exclude)}
						{canFilterOut}
					/>
					<AttributeTable title={m.spanDetail_spanAttributesTitle()} attributes={sections.span} {...pinProps} onFilter={filterInto(span, 'Span')} {canFilterOut} />
					<AttributeTable title={m.spanDetail_resourceAttributesTitle()} attributes={sections.resource} {...pinProps} onFilter={filterInto(span, 'Resource')} {canFilterOut} />
					<AttributeTable title={m.spanDetail_scopeAttributesTitle()} attributes={sections.scope} {...pinProps} onFilter={filterInto(span, 'Scope')} {canFilterOut} />

					{#if span.events.length > 0}
						<div>
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{m.spanDetail_eventsTitle()} ({span.events.length})</h3>
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
							<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{m.spanDetail_linksTitle()} ({span.links.length})</h3>
							<div class="flex flex-col gap-2">
								{#each span.links as link, i (i)}
									<div class="rounded-md border p-2">
										<div class="flex items-baseline justify-between gap-2">
											<span class="truncate font-mono text-xs">
												{link.traceId} / {link.spanId}
											</span>
											<Button variant="ghost" size="xs" class="shrink-0" href={withBase(`/traces/${link.traceId}`)}>
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
										<p class="mt-1 truncate text-sm"><AnsiText text={log.body} /></p>
									</div>
								{/each}
							</div>
							<Button variant="link" size="sm" class="mt-1 h-auto px-0" href={buildSpanLogsHref(span, LINKED_LOGS_MARGIN_MS)}>
								{m.spanDetail_openInLogs()}
							</Button>
						</div>
					{/if}
				</div>
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
