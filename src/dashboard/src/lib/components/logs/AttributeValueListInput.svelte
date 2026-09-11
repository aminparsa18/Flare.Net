<script lang="ts">
	// Chip editor for AttributeFilter/SpanAttributeFilter's `In`/`NotIn` operators - the
	// multi-value counterpart to AttributeValueCombobox's single-value input. Lives under
	// `components/logs/` and is imported by SpanAttributeFiltersRow.svelte too - same
	// cross-page reuse convention AttributeValueCombobox/PopoverMultiSelect already
	// establish.
	//
	// Built on top of AttributeValueCombobox rather than a plain `<Input>` so the "one of"
	// list still gets the same observed-value suggestions as the single-value operators -
	// only the commit behavior differs: picking a suggestion (`onPick`) or pressing
	// Enter/comma (`onCommit`) adds the current draft as a chip and clears the draft,
	// instead of replacing a single committed value.
	import AttributeValueCombobox, { type AttributeValueSuggestion } from './AttributeValueCombobox.svelte';
	import { Badge } from '$lib/components/ui/badge';
	import { cn } from '$lib/utils';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	let {
		values,
		placeholder,
		class: className,
		onChange,
		fetchSuggestions
	}: {
		values: string[];
		placeholder?: string;
		class?: string;
		onChange: (next: string[]) => void;
		fetchSuggestions: (text: string, signal: AbortSignal) => Promise<AttributeValueSuggestion[]>;
	} = $props();

	let draft = $state('');

	/** Trims, ignores empty/already-present drafts (no point in a blank or duplicate chip), otherwise appends and clears the draft. */
	function commitDraft(): void {
		const next = draft.trim();
		draft = '';
		if (!next || values.includes(next)) return;
		onChange([...values, next]);
	}

	function removeAt(index: number): void {
		onChange(values.filter((_, i) => i !== index));
	}
</script>

<div class={cn('flex flex-wrap items-center gap-1', className)}>
	{#each values as v, i (v)}
		<Badge variant="outline" class="gap-1">
			{v}
			<button type="button" onclick={() => removeAt(i)} aria-label={m.attributeFilters_removeValue()}>
				<XIcon class="size-3" />
			</button>
		</Badge>
	{/each}
	<AttributeValueCombobox
		class="h-7 w-28 text-xs"
		value={draft}
		placeholder={placeholder ?? m.attributeFilters_valuesPlaceholder()}
		oninput={(v) => (draft = v)}
		onPick={commitDraft}
		onCommit={commitDraft}
		{fetchSuggestions}
	/>
</div>
