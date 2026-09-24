<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Separator } from '$lib/components/ui/separator';
	import AttributeTable from './AttributeTable.svelte';
	import StackTraceViewer from './StackTraceViewer.svelte';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import { severityVariant } from '$lib/logs/severity';
	import { logsExplorerContext } from '$lib/logs/context';
	import { pinnedAttributes } from '$lib/logs/pinned-attributes.svelte';
	import { formatDurationNano } from '$lib/traces/duration';
	import * as m from '$lib/paraglide/messages';
	import type { AttributeBag } from '$lib/api';

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

	function filterInto(bag: AttributeBag | ((key: string) => AttributeBag | undefined)) {
		return (key: string, value: string, exclude: boolean) => {
			const resolved = typeof bag === 'function' ? bag(key) : bag;
			if (resolved) explorer.addAttributeValueFilter(resolved, key, value, exclude);
		};
	}

	function formatTimestamp(iso: string): string {
		// Every field spelled out: passing fractionalSecondDigits alone switches off toLocaleString's date/time defaults.
		return new Date(iso).toLocaleString(undefined, {
			hour12: false,
			year: 'numeric',
			month: 'numeric',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit',
			fractionalSecondDigits: 3
		});
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
</script>

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
					<Button
						variant="outline"
						size="sm"
						class="ml-auto"
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
					<p class="text-sm break-words whitespace-pre-wrap">{event.body}</p>

					<Separator />

					<div class="grid grid-cols-3 gap-2 text-xs">
						<div>
							<span class="text-muted-foreground">{m.eventDetail_traceId()}</span>
							{#if event.traceId}
								<p class="truncate">
									<a href="/traces/{event.traceId}" class="hover:text-primary font-mono underline-offset-2 hover:underline">
										{event.traceId}
									</a>
								</p>
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

					<AttributeTable
						title={m.eventDetail_pinnedAttributes()}
						attributes={attributeSections.pinned}
						{...pinProps}
						onFilter={filterInto((key) => attributeSections.pinnedBags.get(key))}
					/>
					<AttributeTable title={m.eventDetail_logAttributes()} attributes={attributeSections.log} {...pinProps} onFilter={filterInto('Log')} />
					<AttributeTable
						title={m.eventDetail_resourceAttributes()}
						attributes={attributeSections.resource}
						{...pinProps}
						onFilter={filterInto('Resource')}
					/>
					<AttributeTable title={m.eventDetail_scopeAttributes()} attributes={attributeSections.scope} {...pinProps} onFilter={filterInto('Scope')} />
				</div>
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
