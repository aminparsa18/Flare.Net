<script lang="ts">
	// The Logs "context" view/permalink - shows the events immediately before/after one
	// anchor event (LogsExplorerState.contextView), unfiltered, via /api/logs/context. See
	// docs-internal/planning/roadmap.md's former "Logs context view + permalink" entry and
	// the SigNoz feature it cites (signoz#3190) for the prior art this follows.
	//
	// Reuses LogRow wholesale rather than a bespoke row - same CSS-custom-property grid
	// alignment trick LogTable.svelte already documents on its own wrapper. `live={true}`
	// is passed to every row here (regardless of anything live-tail related) purely to get
	// LogRow's "no Duration column" layout: GetContextAsync never populates
	// SpanDurationNano (see its own remarks), so a Duration column here would just be a
	// column of "—" - LogRow's existing `live` prop already means exactly "leave the
	// Duration column out", it's just being reused for a second reason it happens to fit.
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import LogRow from './LogRow.svelte';
	import LinkIcon from '@lucide/svelte/icons/link';
	import CheckIcon from '@lucide/svelte/icons/check';
	import { logsExplorerContext } from '$lib/logs/context';
	import { buildLogContextDeepLinkHref } from '$lib/deep-links';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.get();

	let copied = $state(false);
	let copiedTimeout: ReturnType<typeof setTimeout> | undefined;

	async function copyLink(): Promise<void> {
		const view = explorer.contextView;
		if (!view) return;
		const href = buildLogContextDeepLinkHref({ eventId: view.anchorEventId, timestamp: view.anchorTimestamp });
		await navigator.clipboard.writeText(`${location.origin}${href}`);
		copied = true;
		clearTimeout(copiedTimeout);
		copiedTimeout = setTimeout(() => (copied = false), 1500);
	}

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}
</script>

<Sheet.Root
	open={(explorer.contextView !== null || explorer.contextLoading) && explorer.selectedEventId === null}
	onOpenChange={(next) => {
		if (!next) explorer.closeContext();
	}}
>
	<!-- `&& explorer.selectedEventId === null` - a row here can open EventDetailSheet
	     (see LogRow's onSelect below), and two independent Sheet.Root instances open at
	     once don't compose cleanly (see EventDetailSheet's "View context" button for the
	     live-verified pointer-interception bug this caused). Excluding this sheet
	     whenever a detail sheet is open - rather than nulling contextView the moment a
	     row is clicked - means closing that detail sheet naturally reopens this one, with
	     no extra bookkeeping in either component. -->
	<Sheet.Content class="flex w-full flex-col sm:max-w-3xl">
		<Sheet.Header>
			<!-- pr-12 - see EventDetailSheet's "View context" button remarks: Sheet.Content's
			     default close "X" is absolutely positioned, not part of this row's flow, and
			     without this the "Copy link" button below renders under it (live-verified). -->
			<Sheet.Title class="flex items-center justify-between gap-2 pr-12">
				<span>{m.logContext_title()}</span>
				{#if explorer.contextView}
					<Button
						variant="outline"
						size="icon-sm"
						title={copied ? m.logContext_copied() : m.logContext_copyLink()}
						onclick={copyLink}
					>
						{#if copied}
							<CheckIcon />
						{:else}
							<LinkIcon />
						{/if}
					</Button>
				{/if}
			</Sheet.Title>
			{#if explorer.contextView}
				<Sheet.Description>{formatTimestamp(explorer.contextView.anchorTimestamp)}</Sheet.Description>
			{/if}
		</Sheet.Header>

		{#if explorer.contextLoading}
			<div class="flex flex-1 items-center justify-center">
				<Spinner />
			</div>
		{:else if explorer.contextError}
			<div class="text-destructive flex flex-1 items-center justify-center px-4 text-center text-sm">
				{explorer.contextError}
			</div>
		{:else if explorer.contextView}
			{@const view = explorer.contextView}
			<div class="flex min-h-0 flex-1 flex-col" style:--log-row-columns="170px 90px 160px 1fr" style:--log-row-height="32px">
				<ScrollArea class="min-h-0 flex-1">
					<div class="flex justify-center border-b py-2">
						{#if view.hasMoreBefore}
							<Button
								variant="ghost"
								size="sm"
								disabled={explorer.contextLoadingMore !== null}
								onclick={() => explorer.loadMoreContextBefore()}
							>
								{#if explorer.contextLoadingMore === 'before'}
									<Spinner class="size-3" />
								{:else}
									{m.logContext_loadEarlier()}
								{/if}
							</Button>
						{:else}
							<span class="text-muted-foreground text-xs">{m.logContext_start()}</span>
						{/if}
					</div>

					{#each view.events as event (event.eventId)}
						<div class={event.eventId === view.anchorEventId ? 'bg-primary/10 border-primary/50 border-l-2' : ''}>
							<!-- setTimeout, not a direct assignment - see EventDetailSheet's "View
							     context" button for the dialog-swap race this avoids (this sheet
							     closes synchronously via the reactive `open` condition above;
							     opening EventDetailSheet needs the same one-tick defer). -->
							<LogRow {event} live={true} onSelect={(e) => setTimeout(() => (explorer.selectedEventId = e.eventId), 0)} />
						</div>
					{/each}

					<div class="flex justify-center border-t py-2">
						{#if view.hasMoreAfter}
							<Button
								variant="ghost"
								size="sm"
								disabled={explorer.contextLoadingMore !== null}
								onclick={() => explorer.loadMoreContextAfter()}
							>
								{#if explorer.contextLoadingMore === 'after'}
									<Spinner class="size-3" />
								{:else}
									{m.logContext_loadLater()}
								{/if}
							</Button>
						{:else}
							<span class="text-muted-foreground text-xs">{m.logContext_end()}</span>
						{/if}
					</div>
				</ScrollArea>
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
