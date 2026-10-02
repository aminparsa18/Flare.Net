<script lang="ts">
	import { withBase } from '$lib/paths';
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { TraceDetailState } from '$lib/traces/trace-state.svelte';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import TraceWaterfall from '$lib/components/traces/TraceWaterfall.svelte';
	import TraceFlameGraph from '$lib/components/traces/TraceFlameGraph.svelte';
	import ServiceMap from '$lib/components/traces/ServiceMap.svelte';
	import SpanDetailSheet from '$lib/components/traces/SpanDetailSheet.svelte';
	import { Button, buttonVariants } from '$lib/components/ui/button';
	import * as Empty from '$lib/components/ui/empty';
	import * as Alert from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import { cn } from '$lib/utils';
	import { Input } from '$lib/components/ui/input';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import { downloadBlob, traceJsonFilename, traceToJson } from '$lib/traces/export';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import SearchIcon from '@lucide/svelte/icons/search';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import XIcon from '@lucide/svelte/icons/x';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import * as m from '$lib/paraglide/messages';

	const detail = traceDetailContext.set(new TraceDetailState());

	// Local to this page, not TraceDetailState - view-local UI state with no other
	// consumer, same "doesn't belong on the shared state class" call SpanDetailSheet's
	// own linked-logs state made (see v5's Planning.md entry).
	let activeTab = $state<'waterfall' | 'flame-graph' | 'service-map'>('waterfall');

	// page.params.traceId is fixed for this component's lifetime - SvelteKit remounts
	// (not just re-renders) a dynamic-segment route when the param changes, since the
	// route's own key includes the segment value, so a plain onMount (not an $effect
	// re-running on param change) is enough here, same as every other page in this app
	// fetching its own data client-side in onMount.
	onMount(() => {
		void detail.load(page.params.traceId!, page.url.searchParams.get('span'));
	});

	function downloadJson(): void {
		const trace = detail.trace;
		if (!trace) return;
		const json = traceToJson(trace, detail.partialSpanIds);
		downloadBlob(new Blob([json], { type: 'application/json;charset=utf-8' }), traceJsonFilename(trace.traceId));
	}

	onDestroy(() => {
		detail.dispose();
	});
</script>

<svelte:head>
	<title>{m.tracePage_title({ traceId: page.params.traceId! })}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="bg-background sticky top-0 z-10 flex items-center gap-2 border-b px-4 py-2">
		<Button variant="ghost" size="sm" href={withBase('/traces')}>
			<ArrowLeftIcon data-icon="inline-start" />
			{m.tracePage_backToTraces()}
		</Button>
		<span class="text-muted-foreground truncate font-mono text-xs">{page.params.traceId}</span>
		{#if !detail.loading && !detail.notFound && !detail.error}
			<!-- Span search over the waterfall and flame graph (the service map has no
			     per-span rows to highlight). Enter / Shift+Enter step through matches,
			     Escape clears. -->
			{#if activeTab !== 'service-map'}
				<div class="ml-auto flex items-center gap-1">
					<div class="relative w-56">
						<SearchIcon class="text-muted-foreground pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2" />
						<Input
							class="h-8 pr-7 pl-8"
							placeholder={m.traceSearch_placeholder()}
							aria-label={m.traceSearch_placeholder()}
							value={detail.spanSearch}
							oninput={(e) => detail.setSpanSearch(e.currentTarget.value)}
							onkeydown={(e) => {
								if (e.key === 'Enter') {
									e.preventDefault();
									detail.stepSpanSearch(e.shiftKey ? -1 : 1);
								} else if (e.key === 'Escape' && detail.spanSearch) {
									e.preventDefault();
									detail.setSpanSearch('');
								}
							}}
						/>
						{#if detail.spanSearch}
							<button
								type="button"
								class="text-muted-foreground hover:text-foreground absolute top-1/2 right-2 -translate-y-1/2"
								aria-label={m.traceSearch_clear()}
								title={m.traceSearch_clear()}
								onclick={() => detail.setSpanSearch('')}
							>
								<XIcon class="size-3.5" />
							</button>
						{/if}
					</div>
					{#if detail.spanSearchActive}
						<span class="text-muted-foreground px-1 text-xs whitespace-nowrap tabular-nums" aria-live="polite">
							{detail.spanSearchMatches.length === 0
								? m.traceSearch_noMatches()
								: m.traceSearch_matchCount({ current: detail.spanSearchIndex + 1, count: detail.spanSearchMatches.length })}
						</span>
						<Button
							variant="ghost"
							size="icon-sm"
							aria-label={m.traceSearch_previous()}
							title={m.traceSearch_previous()}
							disabled={detail.spanSearchMatches.length === 0}
							onclick={() => detail.stepSpanSearch(-1)}
						>
							<ChevronUpIcon />
						</Button>
						<Button
							variant="ghost"
							size="icon-sm"
							aria-label={m.traceSearch_next()}
							title={m.traceSearch_next()}
							disabled={detail.spanSearchMatches.length === 0}
							onclick={() => detail.stepSpanSearch(1)}
						>
							<ChevronDownIcon />
						</Button>
					{/if}
				</div>
			{/if}
			<div class="flex items-center gap-1 {activeTab === 'service-map' ? 'ml-auto' : ''}">
				<button
					type="button"
					class={cn(buttonVariants({ variant: activeTab === 'waterfall' ? 'secondary' : 'ghost', size: 'sm' }))}
					onclick={() => (activeTab = 'waterfall')}
				>
					{m.tracePage_waterfallTab()}
				</button>
				<button
					type="button"
					class={cn(buttonVariants({ variant: activeTab === 'flame-graph' ? 'secondary' : 'ghost', size: 'sm' }))}
					onclick={() => (activeTab = 'flame-graph')}
				>
					{m.tracePage_flameGraphTab()}
				</button>
				<button
					type="button"
					class={cn(buttonVariants({ variant: activeTab === 'service-map' ? 'secondary' : 'ghost', size: 'sm' }))}
					onclick={() => (activeTab = 'service-map')}
				>
					{m.tracePage_serviceMapTab()}
				</button>
			</div>
			<Button
				variant="outline"
				size="icon-sm"
				aria-label={m.tracePage_downloadJson()}
				title={detail.partialSpanIds.size > 0 ? m.tracePage_downloadJsonPartial({ count: detail.trace?.spans.length ?? 0 }) : m.tracePage_downloadJson()}
				onclick={downloadJson}
			>
				<DownloadIcon />
			</Button>
		{/if}
	</div>

	{#if detail.loading}
		<div class="flex flex-1 items-center justify-center">
			<Spinner />
		</div>
	{:else if detail.notFound}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>{m.tracePage_notFoundTitle()}</Empty.Title>
				<Empty.Description>{m.tracePage_notFoundDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if detail.error}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>{m.tracePage_errorTitle()}</Empty.Title>
				<Empty.Description>{detail.error}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if detail.trace?.truncated}
			<Alert.Root class="mx-4 mt-3 w-auto">
				<TriangleAlertIcon />
				<Alert.Title>{m.tracePage_truncatedTitle()}</Alert.Title>
				<Alert.Description>{m.tracePage_truncatedDescription({ count: detail.trace.spans.length.toLocaleString() })}
					{#if detail.childLoadError}<span class="text-destructive"> {detail.childLoadError}</span>{/if}
				</Alert.Description>
			</Alert.Root>
		{/if}
		{#if activeTab === 'waterfall'}
		<TraceWaterfall />
	{:else if activeTab === 'flame-graph'}
		<TraceFlameGraph />
	{:else}
		<ServiceMap spans={detail.trace?.spans ?? []} />
		{/if}
	{/if}
</div>
<SpanDetailSheet />
