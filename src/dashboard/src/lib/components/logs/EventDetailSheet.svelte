<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Separator } from '$lib/components/ui/separator';
	import AttributeTable from './AttributeTable.svelte';
	import { severityVariant } from '$lib/logs/severity';
	import { logsExplorerContext } from '$lib/logs/context';

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
</script>

<Sheet.Root
	open={explorer.selectedEvent !== null}
	onOpenChange={(next) => {
		if (!next) explorer.selectedEventId = null;
	}}
>
	<Sheet.Content class="flex w-full flex-col sm:max-w-lg">
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

					<div class="grid grid-cols-2 gap-2 text-xs">
						<div>
							<span class="text-muted-foreground">Trace ID</span>
							<p class="truncate font-mono">{event.traceId || '—'}</p>
						</div>
						<div>
							<span class="text-muted-foreground">Span ID</span>
							<p class="truncate font-mono">{event.spanId || '—'}</p>
						</div>
					</div>

					{#if exceptionInfo}
						<div class="border-destructive/50 bg-destructive/5 rounded-md border p-3">
							<p class="text-destructive text-sm font-medium">{exceptionInfo.type}</p>
							{#if exceptionInfo.message}
								<p class="mt-1 text-sm">{exceptionInfo.message}</p>
							{/if}
							{#if exceptionInfo.stacktrace}
								<pre class="mt-2 overflow-x-auto text-xs whitespace-pre">{exceptionInfo.stacktrace}</pre>
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
