<script lang="ts">
	// Recursive collapsible tree for a parsed JSON log body. Filtering is limited to what
	// BodyJsonFilter can express (see $lib/logs/json-body.ts): ClickHouse's JSONExtractString
	// only returns string leaves, so string leaves get Equals/NotEquals, scalars inside an
	// array-of-scalars get Has/NotHas on the array's path; numbers/booleans/null are copy-only.
	import JsonTree from './JsonTree.svelte';
	import { bodyJsonPath, displayJsonPath, type JsonValue } from '$lib/logs/json-body';
	import type { BodyJsonFilterOperator } from '$lib/api';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import FunnelPlusIcon from '@lucide/svelte/icons/funnel-plus';
	import FunnelXIcon from '@lucide/svelte/icons/funnel-x';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import * as m from '$lib/paraglide/messages';

	let {
		value,
		label,
		segments = [],
		objectPath = [],
		depth = 0,
		onFilter
	}: {
		value: JsonValue;
		/** Key (object member) or index (array element); undefined for the root. */
		label?: string | number;
		/** Full path of this node, for copy-path (keys and array indices). */
		segments?: (string | number)[];
		/** Object-key-only chain to the nearest enclosing container; null once an array is crossed or a key isn't addressable. */
		objectPath?: string[] | null;
		depth?: number;
		onFilter?: (path: string, operator: BodyJsonFilterOperator, value: string) => void;
	} = $props();

	const isContainer = $derived(typeof value === 'object' && value !== null);
	const entries = $derived<[string | number, JsonValue][]>(
		Array.isArray(value) ? value.map((v, i) => [i, v]) : isContainer ? Object.entries(value as Record<string, JsonValue>) : []
	);
	// Top two levels open by default; deeper ones start collapsed so a big body isn't one wall.
	// svelte-ignore state_referenced_locally
	let open = $state(depth < 2);

	// Filter target of this node: [path, isArrayElement] or null.
	const target = $derived.by((): { path: string; element: boolean } | null => {
		if (typeof value !== 'string') return null;
		if (typeof label === 'number') {
			// Element of an array: addressable only when the array itself sits on an object-key path.
			const path = objectPath ? bodyJsonPath(objectPath) : null;
			return path ? { path, element: true } : null;
		}
		const path = objectPath ? bodyJsonPath(objectPath) : null;
		return path ? { path, element: false } : null;
	});

	let copied = $state<'value' | 'path' | null>(null);
	let resetTimer: ReturnType<typeof setTimeout> | undefined;

	async function copy(kind: 'value' | 'path'): Promise<void> {
		const text = kind === 'path' ? displayJsonPath(segments) : isContainer ? JSON.stringify(value, null, 2) : String(value);
		await navigator.clipboard.writeText(text);
		copied = kind;
		clearTimeout(resetTimer);
		resetTimer = setTimeout(() => (copied = null), 1500);
	}

	function childObjectPath(key: string | number): string[] | null {
		// Array elements share the array's own path (for Has/NotHas), but only one array level deep;
		// anything inside an array element (object members, nested arrays) has no addressable path.
		if (typeof label === 'number') return null;
		if (typeof key === 'number') return objectPath;
		return objectPath ? [...objectPath, key] : null;
	}

	const summary = $derived(Array.isArray(value) ? `[${entries.length}]` : `{${entries.length}}`);
	const scalarClass = $derived(
		typeof value === 'string'
			? 'text-emerald-600 dark:text-emerald-400'
			: typeof value === 'number'
				? 'text-sky-600 dark:text-sky-400'
				: 'text-amber-600 dark:text-amber-400'
	);
</script>

<div class="font-mono text-xs">
	<div class="group hover:bg-muted/50 flex items-start gap-1 rounded px-1 py-0.5" style:padding-left="{depth * 14 + 4}px">
		{#if isContainer && entries.length > 0}
			<button type="button" class="text-muted-foreground mt-0.5 shrink-0" aria-expanded={open} onclick={() => (open = !open)}>
				<ChevronRightIcon class="size-3 transition-transform {open ? 'rotate-90' : ''}" />
			</button>
		{:else}
			<span class="size-3 shrink-0"></span>
		{/if}
		{#if label !== undefined}
			<span class="text-muted-foreground shrink-0">{label}:</span>
		{/if}
		{#if isContainer}
			<button type="button" class="text-muted-foreground" onclick={() => (open = !open)}>{summary}</button>
		{:else}
			<span class="{scalarClass} min-w-0 break-all whitespace-pre-wrap">{typeof value === 'string' ? JSON.stringify(value) : String(value)}</span>
		{/if}
		<span class="ml-auto flex shrink-0 gap-0.5 opacity-0 group-hover:opacity-100 focus-within:opacity-100">
			{#if onFilter && target && typeof value === 'string'}
				<button
					type="button"
					class="text-muted-foreground hover:text-foreground"
					title={m.eventDetail_filterForValue()}
					onclick={() => onFilter(target.path, target.element ? 'Has' : 'Equals', value as string)}
				>
					<FunnelPlusIcon class="size-3" />
				</button>
				<button
					type="button"
					class="text-muted-foreground hover:text-foreground"
					title={m.eventDetail_filterOutValue()}
					onclick={() => onFilter(target.path, target.element ? 'NotHas' : 'NotEquals', value as string)}
				>
					<FunnelXIcon class="size-3" />
				</button>
			{/if}
			<button type="button" class="text-muted-foreground hover:text-foreground" title={m.eventDetail_jsonCopyValue()} onclick={() => copy('value')}>
				{#if copied === 'value'}<CheckIcon class="size-3" />{:else}<CopyIcon class="size-3" />{/if}
			</button>
			{#if segments.length > 0}
				<button type="button" class="text-muted-foreground hover:text-foreground text-[10px]" title={m.eventDetail_jsonCopyPath()} onclick={() => copy('path')}>
					{#if copied === 'path'}<CheckIcon class="size-3" />{:else}.path{/if}
				</button>
			{/if}
		</span>
	</div>
	{#if isContainer && open}
		{#each entries as [key, child] (key)}
			<JsonTree
				value={child}
				label={key}
				segments={[...segments, key]}
				objectPath={childObjectPath(key)}
				depth={depth + 1}
				{onFilter}
			/>
		{/each}
	{/if}
</div>
