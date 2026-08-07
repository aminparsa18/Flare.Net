<script lang="ts" module>
	import type { Snippet } from 'svelte';

	export interface VirtualListProps<T> {
		items: T[];
		/** Fixed row height in px - this list assumes uniform row height (dense log rows), not variable/measured. */
		itemHeight: number;
		/** Per svelte-best-practices: keyed each blocks must use a real identity, not the index (which shifts on every live-tail prepend). */
		getKey: (item: T, index: number) => string | number;
		overscan?: number;
		/** Fires (repeatedly, while still within threshold) once scroll nears the bottom - callers debounce/guard on their own loading state. */
		onEndReached?: () => void;
		endReachedThreshold?: number;
		children: Snippet<[item: T, index: number]>;
		class?: string;
	}
</script>

<script lang="ts" generics="T">
	import { cn } from '$lib/utils';

	let {
		items,
		itemHeight,
		getKey,
		overscan = 8,
		onEndReached,
		endReachedThreshold = 200,
		children,
		class: className
	}: VirtualListProps<T> = $props();

	let containerEl = $state<HTMLDivElement | null>(null);
	let scrollTop = $state(0);
	let containerHeight = $state(0);

	const totalHeight = $derived(items.length * itemHeight);
	const startIndex = $derived(Math.max(0, Math.floor(scrollTop / itemHeight) - overscan));
	const visibleCount = $derived(Math.ceil(containerHeight / itemHeight) + overscan * 2);
	const endIndex = $derived(Math.min(items.length, startIndex + visibleCount));
	const visibleItems = $derived(items.slice(startIndex, endIndex));
	const offsetY = $derived(startIndex * itemHeight);

	// Plain event attribute for the scroll listener (direct user-interaction handling),
	// not $effect+addEventListener - $effect is reserved below for the ResizeObserver,
	// which is genuinely "observing something external to Svelte."
	function handleScroll(e: Event) {
		const el = e.currentTarget as HTMLDivElement;
		scrollTop = el.scrollTop;
		if (onEndReached && el.scrollHeight - el.scrollTop - el.clientHeight < endReachedThreshold) {
			onEndReached();
		}
	}

	$effect(() => {
		if (!containerEl) return;
		containerHeight = containerEl.clientHeight; // avoid a 0-height flash before the first observer callback
		const observer = new ResizeObserver((entries) => {
			containerHeight = entries[0].contentRect.height;
		});
		observer.observe(containerEl);
		return () => observer.disconnect();
	});
</script>

<div bind:this={containerEl} class={cn('relative overflow-y-auto', className)} onscroll={handleScroll}>
	<div style="height: {totalHeight}px; position: relative;">
		<div style="transform: translateY({offsetY}px);">
			{#each visibleItems as item, i (getKey(item, startIndex + i))}
				{@render children(item, startIndex + i)}
			{/each}
		</div>
	</div>
</div>
