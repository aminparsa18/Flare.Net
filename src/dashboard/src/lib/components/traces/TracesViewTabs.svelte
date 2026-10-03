<script lang="ts">
	import { withBase } from '$lib/paths';
	// Switches the /traces page between the trace list (search over individual traces)
	// and the Services RED-metrics rollup (Rate/Errors/Duration aggregated by
	// ServiceName) - folded into this page rather than given its own top-level route,
	// since both views read the same underlying data (trace spans) and "which service
	// looks unhealthy -> drill into its traces" is a natural flow within one page. Same
	// local-tab-state, plain buttonVariants styling as the trace-detail page's own
	// Waterfall/Service map tabs (traces/[traceId]/+page.svelte) - not the shadcn Tabs
	// component, for the same reason that page doesn't use it either (no doc comment
	// there explaining why, but keeping this page's tab UI consistent with the sibling
	// page's own precedent matters more than picking a "more correct" primitive).
	//
	// Funnels is its own route (/traces/funnels - it has saved views of its own), so it's
	// always a link. On that page there's no `onTabChange`, and Traces/Services become links
	// back to /traces (`?tab=services` opens the Services tab there).
	import { buttonVariants } from '$lib/components/ui/button';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	type Tab = 'traces' | 'services';

	interface Props {
		activeTab: Tab | 'funnels' | 'n-plus-one';
		onTabChange?: (tab: Tab) => void;
	}

	let { activeTab, onTabChange }: Props = $props();

	const tabs: { tab: Tab; href: string; label: () => string }[] = [
		{ tab: 'traces', href: withBase('/traces'), label: m.tracesPage_tracesTab },
		{ tab: 'services', href: withBase('/traces?tab=services'), label: m.tracesPage_servicesTab }
	];

	function tabClass(active: boolean): string {
		return cn(buttonVariants({ variant: active ? 'secondary' : 'ghost', size: 'sm' }));
	}
</script>

<div class="flex items-center gap-1">
	{#each tabs as { tab, href, label } (tab)}
		{#if onTabChange}
			<button type="button" class={tabClass(activeTab === tab)} onclick={() => onTabChange(tab)}>
				{label()}
			</button>
		{:else}
			<a class={tabClass(activeTab === tab)} {href}>{label()}</a>
		{/if}
	{/each}
	<a class={tabClass(activeTab === 'funnels')} href={withBase('/traces/funnels')} aria-current={activeTab === 'funnels' ? 'page' : undefined}>
		{m.tracesPage_funnelsTab()}
	</a>
	<a class={tabClass(activeTab === 'n-plus-one')} href={withBase('/traces/n-plus-one')} aria-current={activeTab === 'n-plus-one' ? 'page' : undefined}>
		{m.tracesPage_nPlusOneTab()}
	</a>
</div>
