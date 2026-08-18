<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { TraceDetailState } from '$lib/traces/trace-state.svelte';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import TraceWaterfall from '$lib/components/traces/TraceWaterfall.svelte';
	import ServiceMap from '$lib/components/traces/ServiceMap.svelte';
	import SpanDetailSheet from '$lib/components/traces/SpanDetailSheet.svelte';
	import { Button, buttonVariants } from '$lib/components/ui/button';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { cn } from '$lib/utils';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';

	const detail = traceDetailContext.set(new TraceDetailState());

	// Local to this page, not TraceDetailState - view-local UI state with no other
	// consumer, same "doesn't belong on the shared state class" call SpanDetailSheet's
	// own linked-logs state made (see v5's Planning.md entry).
	let activeTab = $state<'waterfall' | 'service-map'>('waterfall');

	// page.params.traceId is fixed for this component's lifetime - SvelteKit remounts
	// (not just re-renders) a dynamic-segment route when the param changes, since the
	// route's own key includes the segment value, so a plain onMount (not an $effect
	// re-running on param change) is enough here, same as every other page in this app
	// fetching its own data client-side in onMount.
	onMount(() => {
		void detail.load(page.params.traceId!);
	});

	onDestroy(() => {
		detail.dispose();
	});
</script>

<svelte:head>
	<title>Flare — Trace {page.params.traceId}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="bg-background sticky top-0 z-10 flex items-center gap-2 border-b px-4 py-2">
		<Button variant="ghost" size="sm" href="/traces">
			<ArrowLeftIcon data-icon="inline-start" />
			Back to traces
		</Button>
		<span class="text-muted-foreground truncate font-mono text-xs">{page.params.traceId}</span>
		{#if !detail.loading && !detail.notFound && !detail.error}
			<div class="ml-auto flex items-center gap-1">
				<button
					type="button"
					class={cn(buttonVariants({ variant: activeTab === 'waterfall' ? 'secondary' : 'ghost', size: 'sm' }))}
					onclick={() => (activeTab = 'waterfall')}
				>
					Waterfall
				</button>
				<button
					type="button"
					class={cn(buttonVariants({ variant: activeTab === 'service-map' ? 'secondary' : 'ghost', size: 'sm' }))}
					onclick={() => (activeTab = 'service-map')}
				>
					Service Map
				</button>
			</div>
		{/if}
	</div>

	{#if detail.loading}
		<div class="flex flex-1 items-center justify-center">
			<Spinner />
		</div>
	{:else if detail.notFound}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>Trace not found</Empty.Title>
				<Empty.Description>No spans exist for this trace id.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if detail.error}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>Failed to load trace</Empty.Title>
				<Empty.Description>{detail.error}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if activeTab === 'waterfall'}
		<TraceWaterfall />
	{:else}
		<ServiceMap spans={detail.trace?.spans ?? []} />
	{/if}
</div>
<SpanDetailSheet />
