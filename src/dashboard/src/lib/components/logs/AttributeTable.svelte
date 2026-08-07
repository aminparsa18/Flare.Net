<script lang="ts">
	let { title, attributes }: { title: string; attributes: Record<string, string> } = $props();
	const entries = $derived(Object.entries(attributes));
</script>

{#if entries.length > 0}
	<div>
		<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{title}</h3>
		<div class="rounded-md border text-sm">
			{#each entries as [key, value] (key)}
				<div class="grid grid-cols-[minmax(0,1fr)_minmax(0,2fr)] gap-2 border-b px-2 py-1 last:border-b-0">
					<span class="text-muted-foreground truncate font-mono text-xs">{key}</span>
					<span class="truncate font-mono text-xs">{value}</span>
				</div>
			{/each}
		</div>
	</div>
{/if}
