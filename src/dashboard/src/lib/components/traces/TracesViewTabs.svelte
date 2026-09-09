<script lang="ts">
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
	import { buttonVariants } from '$lib/components/ui/button';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	interface Props {
		activeTab: 'traces' | 'services';
		onTabChange: (tab: 'traces' | 'services') => void;
	}

	let { activeTab, onTabChange }: Props = $props();
</script>

<div class="flex items-center gap-1">
	<button
		type="button"
		class={cn(buttonVariants({ variant: activeTab === 'traces' ? 'secondary' : 'ghost', size: 'sm' }))}
		onclick={() => onTabChange('traces')}
	>
		{m.tracesPage_tracesTab()}
	</button>
	<button
		type="button"
		class={cn(buttonVariants({ variant: activeTab === 'services' ? 'secondary' : 'ghost', size: 'sm' }))}
		onclick={() => onTabChange('services')}
	>
		{m.tracesPage_servicesTab()}
	</button>
</div>
