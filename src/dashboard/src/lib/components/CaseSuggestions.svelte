<script lang="ts">
	import type { CaseSuggestion } from '$lib/case-suggestions';
	import * as m from '$lib/paraglide/messages';

	let { suggestions, onApply }: { suggestions: CaseSuggestion[]; onApply: (s: CaseSuggestion) => void } = $props();
</script>

{#if suggestions.length > 0}
	<div class="flex flex-col items-center gap-1 text-sm">
		<span class="text-muted-foreground">{m.caseSuggestions_title()}</span>
		<div class="flex flex-wrap justify-center gap-2">
			{#each suggestions as s (`${s.filterIndex}:${s.typed}:${s.actual}`)}
				<button
					type="button"
					class="hover:bg-muted rounded-md border px-2 py-1 font-mono text-xs"
					onclick={() => onApply(s)}
				>
					{s.key}={s.actual} <span class="text-muted-foreground">({s.count.toLocaleString()})</span>
				</button>
			{/each}
		</div>
	</div>
{/if}
