<script lang="ts">
	import PinIcon from '@lucide/svelte/icons/pin';
	import PinOffIcon from '@lucide/svelte/icons/pin-off';
	import FunnelPlusIcon from '@lucide/svelte/icons/funnel-plus';
	import FunnelXIcon from '@lucide/svelte/icons/funnel-x';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import ChartColumnStackedIcon from '@lucide/svelte/icons/chart-column-stacked';
	import * as m from '$lib/paraglide/messages';

	let {
		title,
		attributes,
		isPinned,
		onTogglePin,
		onFilter,
		onGroupBy,
		groupedByKey
	}: {
		title: string;
		/** A Map (not just a Record) so a caller can hand over an explicit row order. */
		attributes: Record<string, string> | Map<string, string>;
		/** Both optional - without onTogglePin no pin button renders (e.g. SpanDetailSheet). */
		isPinned?: (key: string) => boolean;
		onTogglePin?: (key: string) => void;
		/** Optional - without it no filter-for/filter-out buttons render (e.g. SpanDetailSheet, which has no Logs explorer filter state to push into). */
		onFilter?: (key: string, value: string, exclude: boolean) => void;
		/** Optional - stacks the Logs volume chart by this row's key; without it no group-by button renders. */
		onGroupBy?: (key: string) => void;
		/** Key the volume chart is currently grouped by (if it's in this table's bag) - that row's button stays visible and pressed. */
		groupedByKey?: string | null;
	} = $props();
	const entries = $derived(attributes instanceof Map ? [...attributes] : Object.entries(attributes));

	/**
	 * Only a whole-value absolute http(s) URL becomes a link - never `javascript:`/`data:`
	 * or any other scheme, since attribute values are arbitrary app-emitted text rendered
	 * into an href, and never a URL merely embedded in a longer value.
	 */
	function linkHref(value: string): string | null {
		if (!/^https?:\/\/\S+$/i.test(value)) return null;
		try {
			const url = new URL(value);
			return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
		} catch {
			return null;
		}
	}

	let copiedKey = $state<string | null>(null);
	let copyResetTimer: ReturnType<typeof setTimeout> | undefined;

	async function copyValue(key: string, value: string): Promise<void> {
		await navigator.clipboard.writeText(value);
		copiedKey = key;
		clearTimeout(copyResetTimer);
		copyResetTimer = setTimeout(() => (copiedKey = null), 1500);
	}

	// Hover/focus-revealed, same as the unpinned pin button, so a table isn't a column of icons.
	const revealClass = 'opacity-0 group-hover:opacity-100 focus-visible:opacity-100';
	const actionClass = 'text-muted-foreground hover:text-foreground shrink-0';
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
				{@const href = linkHref(value)}
				<div class="group col-span-2 grid grid-cols-subgrid gap-2 border-b px-2 py-1 last:border-b-0">
					<span class="text-muted-foreground truncate font-mono text-xs">{key}</span>
					<span class="flex min-w-0 items-center gap-1">
						{#if href}
							<a
								{href}
								target="_blank"
								rel="noopener noreferrer"
								class="text-primary flex-1 truncate font-mono text-xs underline-offset-2 hover:underline"
								title={m.eventDetail_openLink()}>{value}</a
							>
						{:else}
							<span class="flex-1 truncate font-mono text-xs">{value}</span>
						{/if}
						{#if onFilter}
							<button
								type="button"
								class="{actionClass} {revealClass}"
								title={m.eventDetail_filterForValue()}
								aria-label={m.eventDetail_filterForValue()}
								onclick={() => onFilter(key, value, false)}
							>
								<FunnelPlusIcon class="size-3.5" />
							</button>
							<button
								type="button"
								class="{actionClass} {revealClass}"
								title={m.eventDetail_filterOutValue()}
								aria-label={m.eventDetail_filterOutValue()}
								onclick={() => onFilter(key, value, true)}
							>
								<FunnelXIcon class="size-3.5" />
							</button>
						{/if}
						{#if onGroupBy}
							{@const grouped = groupedByKey === key}
							<button
								type="button"
								class="{actionClass} {grouped ? 'text-foreground' : revealClass}"
								title={m.eventDetail_groupByAttribute()}
								aria-label={m.eventDetail_groupByAttribute()}
								aria-pressed={grouped}
								onclick={() => onGroupBy(key)}
							>
								<ChartColumnStackedIcon class="size-3.5" />
							</button>
						{/if}
						<button
							type="button"
							class="{actionClass} {copiedKey === key ? '' : revealClass}"
							title={m.eventDetail_copyValue()}
							aria-label={m.eventDetail_copyValue()}
							onclick={() => copyValue(key, value)}
						>
							{#if copiedKey === key}
								<CheckIcon class="size-3.5" />
							{:else}
								<CopyIcon class="size-3.5" />
							{/if}
						</button>
						{#if onTogglePin}
							{@const pinned = isPinned?.(key) ?? false}
							<!-- Always visible once pinned, as the only visible "this is pinned" cue. -->
							<button
								type="button"
								class="{actionClass} {pinned ? '' : revealClass}"
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
