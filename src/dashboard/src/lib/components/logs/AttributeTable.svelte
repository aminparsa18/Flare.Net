<script lang="ts">
	import PinIcon from '@lucide/svelte/icons/pin';
	import PinOffIcon from '@lucide/svelte/icons/pin-off';
	import * as m from '$lib/paraglide/messages';

	let {
		title,
		attributes,
		isPinned,
		onTogglePin
	}: {
		title: string;
		/** A Map (not just a Record) so a caller can hand over an explicit row order. */
		attributes: Record<string, string> | Map<string, string>;
		/** Both optional - without onTogglePin no pin button renders (e.g. SpanDetailSheet). */
		isPinned?: (key: string) => boolean;
		onTogglePin?: (key: string) => void;
	} = $props();
	const entries = $derived(attributes instanceof Map ? [...attributes] : Object.entries(attributes));
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
				<div class="group col-span-2 grid grid-cols-subgrid gap-2 border-b px-2 py-1 last:border-b-0">
					<span class="text-muted-foreground truncate font-mono text-xs">{key}</span>
					<span class="flex min-w-0 items-center gap-1">
						<span class="flex-1 truncate font-mono text-xs">{value}</span>
						{#if onTogglePin}
							{@const pinned = isPinned?.(key) ?? false}
							<!-- Hover/focus-revealed so an unpinned table isn't a column of icons;
							     always visible once pinned, as the only visible "this is pinned" cue. -->
							<button
								type="button"
								class="text-muted-foreground hover:text-foreground shrink-0 {pinned
									? ''
									: 'opacity-0 group-hover:opacity-100 focus-visible:opacity-100'}"
								title={pinned ? m.eventDetail_unpinAttribute() : m.eventDetail_pinAttribute()}
								aria-label={pinned ? m.eventDetail_unpinAttribute() : m.eventDetail_pinAttribute()}
								aria-pressed={pinned}
								onclick={() => onTogglePin(key)}
							>
								{#if pinned}
									<PinOffIcon class="size-3.5" />
								{:else}
									<PinIcon class="size-3.5" />
								{/if}
							</button>
						{/if}
					</span>
				</div>
			{/each}
		</div>
	</div>
{/if}
