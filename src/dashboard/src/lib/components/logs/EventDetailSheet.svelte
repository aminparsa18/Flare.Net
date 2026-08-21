<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Separator } from '$lib/components/ui/separator';
	import AttributeTable from './AttributeTable.svelte';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import { severityVariant } from '$lib/logs/severity';
	import { logsExplorerContext } from '$lib/logs/context';
	import { formatDurationNano } from '$lib/traces/duration';

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

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
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
	     wrapping constantly. whitespace-pre-wrap (added earlier) is still the actual
	     never-truncates guarantee for the rare line that's wider than even this. -->
	<Sheet.Content class="flex w-full flex-col sm:max-w-5xl">
		{#if explorer.selectedEvent}
			{@const event = explorer.selectedEvent}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					<Badge variant={severityVariant(event.severityNumber)}>{event.severityText || '—'}</Badge>
					{event.serviceName || '—'}
					{#if event.eventName}
						<span class="text-muted-foreground font-normal">· {event.eventName}</span>
					{/if}
				</Sheet.Title>
				<Sheet.Description>{formatTimestamp(event.timestamp)}</Sheet.Description>
			</Sheet.Header>
			<ScrollArea class="min-h-0 flex-1 px-4">
				<div class="flex flex-col gap-4 pb-8">
					<p class="text-sm">{event.body}</p>

					<Separator />

					<div class="grid grid-cols-3 gap-2 text-xs">
						<div>
							<span class="text-muted-foreground">Trace ID</span>
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
							<span class="text-muted-foreground">Span ID</span>
							<p class="truncate font-mono">{event.spanId || '—'}</p>
						</div>
						<div>
							<span class="text-muted-foreground">Span duration</span>
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
									title="Copy exception details"
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
								<!-- whitespace-pre-wrap (not whitespace-pre): keeps the stacktrace's own
								     indentation/line breaks but wraps long lines instead of clipping them
								     behind overflow-x-auto - that was the actual cause of "can't see the
								     whole message," not the sheet's width. -->
								<pre class="mt-2 overflow-x-auto text-xs whitespace-pre-wrap break-words">{exceptionInfo.stacktrace}</pre>
							{/if}
						</div>
					{/if}

					<AttributeTable title="Log attributes" attributes={logAttributesWithoutException} />
					<AttributeTable title="Resource attributes" attributes={event.resourceAttributes} />
					<AttributeTable title="Scope attributes" attributes={event.scopeAttributes} />
				</div>
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
