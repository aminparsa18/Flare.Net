<script lang="ts">
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import { indexingContext } from '$lib/indexing/context';
	import * as m from '$lib/paraglide/messages';

	const indexing = indexingContext.get();
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center justify-between gap-2 border-b px-4 py-2">
	<div>
		<h1 class="text-sm font-semibold">{m.indexingToolbar_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.indexingToolbar_description()}</p>
	</div>
	<Button variant="outline" size="sm" onclick={() => indexing.load()} disabled={indexing.loading}>
		{#if indexing.loading}
			<Spinner class="size-4" data-icon="inline-start" />
		{:else}
			<RefreshCwIcon data-icon="inline-start" />
		{/if}
		{m.indexingToolbar_refresh()}
	</Button>
</div>
