<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import { severityVariant } from '$lib/logs/severity';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { AROUND_DEFAULT_MS, aroundRange } from '$lib/time/around';
	import { explorerPrefs } from '$lib/explorer/prefs.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import EventDetailBody from './EventDetailBody.svelte';
	import * as m from '$lib/paraglide/messages';
	import { formatTimestamp } from '$lib/time/format';

	const explorer = logsExplorerContext.get();

	// j/k and ↑/↓ step through the result list while the drawer is open; ignored while typing
	// in a field or when a modifier is held.
	function onNavKey(e: KeyboardEvent) {
		if (explorer.selectedIndex < 0 || e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented) return;
		const t = e.target as HTMLElement | null;
		if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
		// Inline expansion leaves the list on screen, so the arrows keep scrolling it; only j/k step.
		const arrows = explorerPrefs.rowClickAction === 'panel';
		const delta = e.key === 'j' || (arrows && e.key === 'ArrowDown') ? 1 : e.key === 'k' || (arrows && e.key === 'ArrowUp') ? -1 : 0;
		if (!delta) return;
		e.preventDefault();
		void explorer.selectAdjacent(delta);
	}
</script>

<svelte:window onkeydown={onNavKey} />

<Sheet.Root
	open={explorer.selectedEvent !== null && (explorerPrefs.rowClickAction === 'panel' || explorer.selectedIndex < 0)}
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
							explorer.selectedEventId = null;
							explorer.focusAround(aroundRange(new Date(event.timestamp), AROUND_DEFAULT_MS));
						}}
					>
						<ClockIcon />
						{m.eventDetail_aroundThis()}
					</Button>
					<Button
						variant="outline"
						size="sm"
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
				<EventDetailBody {event} />
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
