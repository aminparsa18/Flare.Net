<script lang="ts" module>
	export interface AttributeValueSuggestion {
		value: string;
		count: number;
	}
</script>

<script lang="ts">
	// Drop-in replacement for a plain `<Input>` in AttributeFiltersRow.svelte's value
	// column - still a free-text field (attribute values aren't a closed set the server can
	// enumerate exhaustively, only observe from real data - see getLogAttributeValues'/
	// getSpanAttributeValues' own "best-effort" remarks), but backed by a dropdown of
	// actually-observed values for the row's current bag+key, most-observed first. Lives
	// under `components/logs/` and is imported by SpanAttributeFiltersRow.svelte too - same
	// cross-page reuse convention `PopoverMultiSelect.svelte` (this directory) already
	// establishes, since the two pages' attribute-filter builders are otherwise identical.
	//
	// Deliberately not built on `$lib/components/ui/command` (bits-ui's Command primitive):
	// that component owns its own search-text state and client-side substring filtering,
	// neither of which fits here - the "search text" and the row's committed `value` need
	// to be the exact same string (a plain, always-editable input), and filtering is
	// server-driven (getLogAttributeValues' `prefix`), not a static in-memory list. A
	// minimal hand-rolled dropdown, anchored to the input via ordinary CSS (`relative` +
	// `absolute`), sidesteps also having to fight a portal-positioned popover's focus-trap
	// behavior mid-keystroke.
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	let {
		value,
		placeholder,
		class: className,
		oninput,
		fetchSuggestions,
		onPick,
		onCommit
	}: {
		value: string;
		placeholder?: string;
		class?: string;
		oninput: (value: string) => void;
		/**
		 * Looks up candidates for the row's current bag+key, narrowed by `text` (what's
		 * been typed so far - empty on initial focus, before any typing). Re-run
		 * (debounced) on every keystroke; a rejected/aborted call should just resolve to
		 * `[]` rather than throw, same "non-critical, fewer/no options until a retry"
		 * posture `TracesExplorerState.loadKnownServices` documents for its own best-effort
		 * fetch.
		 */
		fetchSuggestions: (text: string, signal: AbortSignal) => Promise<AttributeValueSuggestion[]>;
		/**
		 * Fired (in addition to `oninput`) when a suggestion is clicked, not when the user
		 * just types - lets a caller distinguish "picked from the list" from "still typing"
		 * without guessing from `oninput` alone. Optional: the single-value row usage
		 * (AttributeFiltersRow/SpanAttributeFiltersRow's Equals/NotEquals/Regex/NotRegex
		 * columns) has no use for it, `oninput` already commits every keystroke there.
		 * `AttributeValueListInput` (the `In`/`NotIn` multi-value column) is the one
		 * consumer that does - it commits a picked suggestion as a chip immediately.
		 */
		onPick?: (value: string) => void;
		/**
		 * Fired on Enter or comma, in addition to `oninput`'s per-keystroke updates.
		 * Optional and unused by the single-value row usage (typing already commits there,
		 * nothing extra happens on Enter); `AttributeValueListInput` uses it to commit the
		 * current draft text as a chip without waiting for a click on a suggestion.
		 */
		onCommit?: () => void;
	} = $props();

	let open = $state(false);
	let loading = $state(false);
	let suggestions = $state<AttributeValueSuggestion[]>([]);

	let fetchAbort: AbortController | null = null;
	let fetchDebounce: ReturnType<typeof setTimeout> | undefined;

	function scheduleFetch(text: string): void {
		clearTimeout(fetchDebounce);
		// Shorter than AttributeFiltersRow's own 300ms commit debounce - this only fetches
		// suggestions, it doesn't re-run the log/span search, so a snappier dropdown costs
		// nothing the commit debounce is protecting against.
		fetchDebounce = setTimeout(() => void runFetch(text), 150);
	}

	async function runFetch(text: string): Promise<void> {
		fetchAbort?.abort();
		const abort = new AbortController();
		fetchAbort = abort;
		loading = true;
		try {
			const results = await fetchSuggestions(text, abort.signal);
			if (abort.signal.aborted) return;
			suggestions = results;
		} catch {
			if (abort.signal.aborted) return;
			suggestions = [];
		} finally {
			if (!abort.signal.aborted) loading = false;
		}
	}

	function handleInput(e: Event & { currentTarget: HTMLInputElement }): void {
		const next = e.currentTarget.value;
		oninput(next);
		open = true;
		scheduleFetch(next);
	}

	function handleFocus(): void {
		open = true;
		if (suggestions.length === 0) scheduleFetch(value);
	}

	// Closes on a delay rather than immediately - a suggestion button's `onmousedown` fires
	// (and calls pick(), below) before this timer runs, so clicking one still lands. The
	// `mousedown`-not-`click` handler on each button is the other half of that: mousedown
	// fires before the input's blur event, `click` would fire after (blur already closed
	// the dropdown, unmounting the button before its click ever arrives).
	function handleBlur(): void {
		setTimeout(() => {
			open = false;
		}, 150);
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.key === 'Escape') {
			open = false;
			return;
		}
		if (onCommit && (e.key === 'Enter' || e.key === ',')) {
			e.preventDefault();
			onCommit();
		}
	}

	function pick(v: string): void {
		clearTimeout(fetchDebounce);
		open = false;
		oninput(v);
		onPick?.(v);
	}
</script>

<div class="relative">
	<Input
		class={className}
		{placeholder}
		{value}
		oninput={handleInput}
		onfocus={handleFocus}
		onblur={handleBlur}
		onkeydown={handleKeydown}
		autocomplete="off"
	/>
	{#if open && (loading || suggestions.length > 0)}
		<div
			class="bg-popover text-popover-foreground ring-foreground/10 absolute top-full left-0 z-50 mt-1 max-h-48 w-56 overflow-auto rounded-md py-1 text-xs shadow-md ring-1"
		>
			{#if loading && suggestions.length === 0}
				<div class="text-muted-foreground flex items-center gap-1.5 px-2 py-1.5">
					<Spinner class="size-3" />
					{m.attributeValueCombobox_loading()}
				</div>
			{:else}
				{#each suggestions as s (s.value)}
					<button
						type="button"
						class={cn('hover:bg-accent hover:text-accent-foreground flex w-full items-center justify-between gap-2 px-2 py-1.5 text-left')}
						onmousedown={(e) => {
							e.preventDefault();
							pick(s.value);
						}}
					>
						<span class="truncate">{s.value}</span>
						<span class="text-muted-foreground tabular-nums">{s.count}</span>
					</button>
				{/each}
			{/if}
		</div>
	{/if}
</div>
