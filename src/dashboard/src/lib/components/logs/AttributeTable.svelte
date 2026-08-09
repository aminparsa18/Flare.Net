<script lang="ts">
	let { title, attributes }: { title: string; attributes: Record<string, string> } = $props();
	const entries = $derived(Object.entries(attributes));
</script>

{#if entries.length > 0}
	<div>
		<h3 class="text-muted-foreground mb-1 text-xs font-medium tracking-wide uppercase">{title}</h3>
		<!-- The old per-row `grid grid-cols-[1fr_2fr]` sized each row's key column off a fixed
		     ratio of that row's own width, independent of every other row - so a long key like
		     "telemetry.sdk.version" truncated while short values like "dotnet" left the value
		     column mostly empty, even though there was plenty of room to just give the key
		     column more space. grid-cols-subgrid makes every row adopt this shared outer grid's
		     column tracks instead of sizing its own - so the key column is exactly as wide as
		     its widest key across ALL rows (capped at 16rem so one pathological key can't crowd
		     out the value column), computed jointly, and rows stay aligned. -->
		<div class="grid grid-cols-[minmax(0,min(max-content,16rem))_minmax(0,1fr)] rounded-md border text-sm">
			{#each entries as [key, value] (key)}
				<div class="col-span-2 grid grid-cols-subgrid gap-2 border-b px-2 py-1 last:border-b-0">
					<span class="text-muted-foreground truncate font-mono text-xs">{key}</span>
					<span class="truncate font-mono text-xs">{value}</span>
				</div>
			{/each}
		</div>
	</div>
{/if}
