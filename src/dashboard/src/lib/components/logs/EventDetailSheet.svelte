<script lang="ts">
	import { withBase } from '$lib/paths';
	import AnsiText from './AnsiText.svelte';
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Separator } from '$lib/components/ui/separator';
	import AttributeTable from './AttributeTable.svelte';
	import StackTraceViewer from './StackTraceViewer.svelte';
	import EventHostMetrics from './EventHostMetrics.svelte';
	import EventPodMetrics from './EventPodMetrics.svelte';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import JsonTree from './JsonTree.svelte';
	import { parseJsonBody } from '$lib/logs/json-body';
	import type { BodyJsonFilterOperator } from '$lib/api';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import { severityVariant } from '$lib/logs/severity';
	import { logsExplorerContext } from '$lib/logs/context';
	import { pinnedAttributes } from '$lib/logs/pinned-attributes.svelte';
	import { formatDurationNano } from '$lib/traces/duration';
	import * as m from '$lib/paraglide/messages';
	import type { AttributeBag } from '$lib/api';
	import { formatTimestamp } from '$lib/time/format';

	const explorer = logsExplorerContext.get();

	// OTel semantic-conventions keys for exceptions recorded on a log record - real,
	// standard keys (e.g. what Serilog.Sinks.OpenTelemetry maps LogEventException into),
	// not something Flare invents. Special-cased into a callout instead of showing up
	// twice (once here, once in the generic log-attributes table below).
	const EXCEPTION_TYPE_KEY = 'exception.type';
	const EXCEPTION_MESSAGE_KEY = 'exception.message';
	const EXCEPTION_STACKTRACE_KEY = 'exception.stacktrace';

	const exceptionInfo = $derived.by(() => {
		const event = explorer.selectedEvent;
		const type = event?.logAttributes[EXCEPTION_TYPE_KEY];
		if (!event || !type) return null;
		return {
			type,
			message: event.logAttributes[EXCEPTION_MESSAGE_KEY],
			stacktrace: event.logAttributes[EXCEPTION_STACKTRACE_KEY]
		};
	});

	const logAttributesWithoutException = $derived.by(() => {
		const event = explorer.selectedEvent;
		if (!event) return {};
		if (!exceptionInfo) return event.logAttributes;
		const rest = { ...event.logAttributes };
		delete rest[EXCEPTION_TYPE_KEY];
		delete rest[EXCEPTION_MESSAGE_KEY];
		delete rest[EXCEPTION_STACKTRACE_KEY];
		return rest;
	});

	// The host whose CPU/memory to chart - `host.name`, or for a Kubernetes pod the node it
	// ran on (`k8s.node.name`, set by the collector's k8sattributes processor): a node's own
	// hostmetrics carry that name as its `host.name`. A pod's logs also get the pod's own
	// charts (kubeletstats) - a pod hitting its limits is a different question from the box
	// starving, so both show.
	const metricsHost = $derived.by(() => {
		const resource = explorer.selectedEvent?.resourceAttributes;
		return resource?.['host.name'] || resource?.['k8s.node.name'] || null;
	});

	const metricsPod = $derived.by(() => {
		const resource = explorer.selectedEvent?.resourceAttributes;
		const name = resource?.['k8s.pod.name'];
		return name ? { name, namespace: resource?.['k8s.namespace.name'] || null } : null;
	});

	// Pinned keys pulled out of whichever bag holds them (log, then resource, then scope)
	// into one Pinned table, in pin order - and dropped from that one bag's own table so
	// the row doesn't render twice. A key that also exists in a later bag (rare) stays
	// visible there, since that's a different value.
	const attributeSections = $derived.by(() => {
		const event = explorer.selectedEvent;
		const log = { ...logAttributesWithoutException };
		const resource = { ...event?.resourceAttributes };
		const scope = { ...event?.scopeAttributes };
		const pinned = new Map<string, string>();
		// Which bag each pinned row came from - the Pinned table mixes bags, and a
		// filter-for/out action needs the right one to build its AttributeFilter.
		const pinnedBags = new Map<string, AttributeBag>();
		const bags: [AttributeBag, Record<string, string>][] = [
			['Log', log],
			['Resource', resource],
			['Scope', scope]
		];
		for (const key of pinnedAttributes.keys) {
			const found = bags.find(([, b]) => key in b);
			if (!found) continue;
			const [bagName, bag] = found;
			pinned.set(key, bag[key]);
			pinnedBags.set(key, bagName);
			delete bag[key];
		}
		return { pinned, pinnedBags, log, resource, scope };
	});

	const pinProps = {
		isPinned: (key: string) => pinnedAttributes.has(key),
		onTogglePin: (key: string) => pinnedAttributes.toggle(key)
	};

	/** Clicking the row that's already grouped toggles grouping back off. */
	function groupByInto(bag: AttributeBag | ((key: string) => AttributeBag | undefined)) {
		return (key: string) => {
			const resolved = typeof bag === 'function' ? bag(key) : bag;
			if (!resolved) return;
			const current = explorer.filter.volumeGroupBy;
			explorer.setVolumeGroupBy(current?.bag === resolved && current.key === key ? null : { bag: resolved, key });
		};
	}

	function replaceInto(bag: AttributeBag | ((key: string) => AttributeBag | undefined)) {
		return (key: string, value: string) => {
			const resolved = typeof bag === 'function' ? bag(key) : bag;
			if (!resolved) return;
			explorer.replaceFiltersWithAttribute(resolved, key, value);
			explorer.selectedEventId = null;
		};
	}

	function groupedKeyIn(bag: AttributeBag): string | null {
		const current = explorer.filter.volumeGroupBy;
		return current?.bag === bag ? current.key : null;
	}

	function filterInto(bag: AttributeBag | ((key: string) => AttributeBag | undefined)) {
		return (key: string, value: string, exclude: boolean) => {
			const resolved = typeof bag === 'function' ? bag(key) : bag;
			if (resolved) explorer.addAttributeValueFilter(resolved, key, value, exclude);
		};
	}

	// A multi-MB body (a dumped payload, a serialized blob) rendered whole through AnsiText
	// can freeze the tab, so only the first BODY_PREVIEW_CHARS render until "Show full body".
	// Keyed by eventId, so opening another event collapses back to the preview.
	const BODY_PREVIEW_CHARS = 64 * 1024;
	let fullBodyEventId = $state<string | null>(null);
	let bodyCopied = $state(false);
	let bodyCopyResetTimer: ReturnType<typeof setTimeout> | undefined;

	const bodyView = $derived.by(() => {
		const event = explorer.selectedEvent;
		const body = event?.body ?? '';
		if (!event || body.length <= BODY_PREVIEW_CHARS || fullBodyEventId === event.eventId) return { text: body, truncated: false };
		return { text: body.slice(0, BODY_PREVIEW_CHARS), truncated: true };
	});

	// JSON tree only when the whole body is on screen (a truncated preview can't parse) and parses
	// to an object/array. `bodyRaw` flips back to the plain/ANSI text; it resets per event.
	let rawBodyEventId = $state<string | null>(null);
	const bodyJson = $derived(bodyView.truncated ? null : parseJsonBody(bodyView.text));
	const showTree = $derived(bodyJson !== null && rawBodyEventId !== explorer.selectedEvent?.eventId);

	function addBodyJsonFilter(path: string, operator: BodyJsonFilterOperator, value: string): void {
		const exists = explorer.filter.bodyJsonFilters.some((f) => f.path === path && f.operator === operator && f.value === value);
		if (exists) return;
		explorer.setBodyJsonFilters([...explorer.filter.bodyJsonFilters, { path, operator, value }]);
	}

	/** Clicking the JSON field the chart is already grouped by toggles grouping back off. */
	function toggleBodyJsonGroupBy(path: string): void {
		const current = explorer.filter.volumeGroupBy;
		explorer.setVolumeGroupBy(current?.bag === 'BodyJson' && current.key === path ? null : { bag: 'BodyJson', key: path });
	}

	function formatSize(chars: number): string {
		return chars >= 1024 * 1024 ? `${(chars / (1024 * 1024)).toFixed(1)} MB` : `${Math.round(chars / 1024)} KB`;
	}

	async function copyBody(body: string): Promise<void> {
		await navigator.clipboard.writeText(body);
		bodyCopied = true;
		clearTimeout(bodyCopyResetTimer);
		bodyCopyResetTimer = setTimeout(() => (bodyCopied = false), 1500);
	}

	let copied = $state(false);
	let copyResetTimer: ReturnType<typeof setTimeout> | undefined;

	async function copyException(info: NonNullable<typeof exceptionInfo>): Promise<void> {
		const text = [info.type, info.message, info.stacktrace].filter(Boolean).join('\n');
		await navigator.clipboard.writeText(text);
		copied = true;
		clearTimeout(copyResetTimer);
		copyResetTimer = setTimeout(() => (copied = false), 1500);
	}

	// j/k and ↑/↓ step through the result list while the drawer is open; ignored while typing
	// in a field or when a modifier is held.
	function onNavKey(e: KeyboardEvent) {
		if (explorer.selectedIndex < 0 || e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented) return;
		const t = e.target as HTMLElement | null;
		if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
		const delta = e.key === 'j' || e.key === 'ArrowDown' ? 1 : e.key === 'k' || e.key === 'ArrowUp' ? -1 : 0;
		if (!delta) return;
		e.preventDefault();
		void explorer.selectAdjacent(delta);
	}
</script>

<svelte:window onkeydown={onNavKey} />

<Sheet.Root
	open={explorer.selectedEvent !== null}
	onOpenChange={(next) => {
		if (!next) explorer.selectedEventId = null;
	}}
>
	<!-- Wide enough that real .NET stack trace lines (often 100-150+ chars with generics/async
	     state machines/file paths) mostly fit on one line at text-xs monospace, instead of
	     wrapping constantly. StackTraceViewer still wraps (never truncates) the rare line
	     that's wider than even this. -->
	<Sheet.Content class="flex w-full flex-col sm:max-w-5xl">
		{#if explorer.selectedEvent}
			{@const event = explorer.selectedEvent}
			<Sheet.Header>
				<!-- pr-12 - Sheet.Content's default close "X" is absolutely positioned
				     (top-4 right-4, ~24px wide), not part of this header's own flex flow, so
				     without this the "View context" button below can render directly under
				     it and steal its clicks (live-verified via playwright-cli). -->
				<Sheet.Title class="flex flex-wrap items-center gap-2 pr-12">
					<Badge variant={severityVariant(event.severityNumber)}>{event.severityText || '—'}</Badge>
					{event.serviceName || '—'}
					{#if event.eventName}
						<span class="text-muted-foreground font-normal">· {event.eventName}</span>
					{/if}
					{#if explorer.selectedIndex >= 0}
						<span class="ml-auto flex items-center gap-1">
							<Button
								variant="outline"
								size="icon-sm"
								title={m.eventDetail_previous()}
								aria-label={m.eventDetail_previous()}
								disabled={explorer.selectedIndex === 0}
								onclick={() => void explorer.selectAdjacent(-1)}
							>
								<ChevronUpIcon />
							</Button>
							<Button
								variant="outline"
								size="icon-sm"
								title={m.eventDetail_next()}
								aria-label={m.eventDetail_next()}
								disabled={explorer.selectedIndex >= explorer.events.length - 1 && !explorer.nextCursor}
								onclick={() => void explorer.selectAdjacent(1)}
							>
								<ChevronDownIcon />
							</Button>
						</span>
					{/if}
					<Button
						variant="outline"
						size="sm"
						class={explorer.selectedIndex >= 0 ? '' : 'ml-auto'}
						onclick={() => {
							// Clears selectedEventId (closing this sheet) *before* opening
							// context - two independent bits-ui Sheet.Root instances open at
							// once don't compose cleanly (the hidden one's close button stayed
							// hit-testable *above* the new one, live-verified via
							// playwright-cli). LogContextSheet's own `open` condition mirrors
							// this by excluding itself whenever selectedEventId is set, so
							// clicking a context row to open its detail - then closing that
							// detail sheet - naturally reopens the context view it came from,
							// with no extra bookkeeping needed in either direction.
							explorer.selectedEventId = null;
							// setTimeout, not a direct call: opening LogContextSheet synchronously
							// within this still-bubbling click handler let its freshly-mounted
							// outside-click listener catch the tail of *this same* click and
							// immediately self-close (live-verified via playwright-cli - the
							// sheet never appeared and /api/logs/context was never even sent,
							// since the abort from that self-close raced the fetch). Deferring to
							// the next tick lets the current click's document-level dispatch
							// finish first, same fix this class of dialog-library race always
							// takes.
							setTimeout(() => void explorer.openContext(event), 0);
						}}
					>
						<ArrowUpDownIcon />
						{m.eventDetail_viewContext()}
					</Button>
				</Sheet.Title>
				<Sheet.Description>{formatTimestamp(event.timestamp)}</Sheet.Description>
			</Sheet.Header>
			<ScrollArea class="min-h-0 flex-1 px-4">
				<div class="flex flex-col gap-4 pb-8">
					{#if bodyJson !== null}
						<div class="flex justify-end">
							<Button variant="outline" size="xs" onclick={() => (rawBodyEventId = showTree ? event.eventId : null)}>
								{showTree ? m.eventDetail_jsonShowRaw() : m.eventDetail_jsonShowTree()}
							</Button>
						</div>
					{/if}
					{#if showTree && bodyJson !== null}
						<JsonTree
							value={bodyJson}
							onFilter={addBodyJsonFilter}
							onGroupBy={toggleBodyJsonGroupBy}
							groupedPath={explorer.filter.volumeGroupBy?.bag === 'BodyJson' ? explorer.filter.volumeGroupBy.key : null}
						/>
					{:else}
						<p class="text-sm break-words whitespace-pre-wrap"><AnsiText text={bodyView.text} /></p>
					{/if}
					{#if bodyView.truncated}
						<div class="text-muted-foreground flex flex-wrap items-center gap-2 text-xs">
							<span>
								{m.eventDetail_bodyTruncated({ shown: formatSize(BODY_PREVIEW_CHARS), total: formatSize(event.body?.length ?? 0) })}
							</span>
							<Button variant="outline" size="xs" onclick={() => (fullBodyEventId = event.eventId)}>
								{m.eventDetail_showFullBody()}
							</Button>
							<Button variant="outline" size="xs" onclick={() => copyBody(event.body ?? '')}>
								{#if bodyCopied}<CheckIcon />{:else}<CopyIcon />{/if}
								{m.eventDetail_copyBody()}
							</Button>
						</div>
					{/if}

					<Separator />

					<div class="grid grid-cols-3 gap-2 text-xs">
						<div>
							<span class="text-muted-foreground">{m.eventDetail_traceId()}</span>
							{#if event.traceId}
								<p class="truncate">
									<a href={withBase(`/traces/${event.traceId}`)} class="hover:text-primary font-mono underline-offset-2 hover:underline">
										{event.traceId}
									</a>
								</p>
								<button
									type="button"
									class="text-primary mt-0.5 underline-offset-2 hover:underline"
									onclick={() => {
										explorer.applyTraceIdFilter(event.traceId);
										explorer.selectedEventId = null;
									}}
								>
									{m.eventDetail_logsForTrace()}
								</button>
							{:else}
								<p class="truncate font-mono">—</p>
							{/if}
						</div>
						<div>
							<span class="text-muted-foreground">{m.eventDetail_spanId()}</span>
							<p class="truncate font-mono">{event.spanId || '—'}</p>
						</div>
						<div>
							<span class="text-muted-foreground">{m.eventDetail_spanDuration()}</span>
							<p class="truncate font-mono">
								{event.spanDurationNano != null ? formatDurationNano(event.spanDurationNano) : '—'}
							</p>
						</div>
					</div>

					{#if exceptionInfo}
						<div class="border-destructive/50 bg-destructive/5 rounded-md border p-3">
							<div class="flex items-start justify-between gap-2">
								<p class="text-destructive text-sm font-medium break-words">{exceptionInfo.type}</p>
								<Button
									variant="ghost"
									size="icon-xs"
									class="text-muted-foreground hover:text-foreground shrink-0"
									title={m.eventDetail_copyException()}
									onclick={() => copyException(exceptionInfo)}
								>
									{#if copied}
										<CheckIcon />
									{:else}
										<CopyIcon />
									{/if}
								</Button>
							</div>
							{#if exceptionInfo.message}
								<p class="mt-1 text-sm break-words">{exceptionInfo.message}</p>
							{/if}
							{#if exceptionInfo.stacktrace}
								<StackTraceViewer trace={exceptionInfo.stacktrace} class="mt-2" />
							{/if}
						</div>
					{/if}

					{#if metricsPod}
						<EventPodMetrics podName={metricsPod.name} namespace={metricsPod.namespace} timestamp={event.timestamp} />
					{/if}
					{#if metricsHost}
						<EventHostMetrics hostName={metricsHost} timestamp={event.timestamp} />
					{/if}

					<AttributeTable
						title={m.eventDetail_pinnedAttributes()}
						attributes={attributeSections.pinned}
						{...pinProps}
						onFilter={filterInto((key) => attributeSections.pinnedBags.get(key))}
						onReplace={replaceInto((key) => attributeSections.pinnedBags.get(key))}
						onGroupBy={groupByInto((key) => attributeSections.pinnedBags.get(key))}
						groupedByKey={explorer.filter.volumeGroupBy &&
						attributeSections.pinnedBags.get(explorer.filter.volumeGroupBy.key) === explorer.filter.volumeGroupBy.bag
							? explorer.filter.volumeGroupBy.key
							: null}
					/>
					<AttributeTable title={m.eventDetail_logAttributes()} attributes={attributeSections.log} {...pinProps}
						onFilter={filterInto('Log')}
						onReplace={replaceInto('Log')}
						onGroupBy={groupByInto('Log')}
						groupedByKey={groupedKeyIn('Log')}
					/>
					<AttributeTable
						title={m.eventDetail_resourceAttributes()}
						attributes={attributeSections.resource}
						{...pinProps}
						onFilter={filterInto('Resource')}
						onReplace={replaceInto('Resource')}
						onGroupBy={groupByInto('Resource')}
						groupedByKey={groupedKeyIn('Resource')}
					/>
					<AttributeTable title={m.eventDetail_scopeAttributes()} attributes={attributeSections.scope} {...pinProps}
						onFilter={filterInto('Scope')}
						onReplace={replaceInto('Scope')}
						onGroupBy={groupByInto('Scope')}
						groupedByKey={groupedKeyIn('Scope')}
					/>
				</div>
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
