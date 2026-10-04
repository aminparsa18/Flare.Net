<script lang="ts" generics="T extends string">
	// A radio group rendered as selectable option cards - theme, nav layout, density and font
	// size all share this shape on the Appearance page.
	import type { Component, Snippet } from 'svelte';
	import { cn } from '$lib/utils';

	interface Option {
		value: T;
		label: string;
		description?: string;
		icon?: Component;
		preview?: Snippet;
	}

	let {
		value,
		options,
		label,
		onchange
	}: { value: T; options: Option[]; label: string; onchange: (value: T) => void } = $props();
</script>

<div role="radiogroup" aria-label={label} class="grid grid-cols-1 gap-3 sm:grid-cols-[repeat(auto-fit,minmax(10rem,1fr))]">
	{#each options as option (option.value)}
		{@const selected = option.value === value}
		<button
			type="button"
			role="radio"
			aria-checked={selected}
			onclick={() => onchange(option.value)}
			class={cn(
				'hover:bg-accent/50 flex cursor-pointer flex-col items-start gap-2 rounded-lg border p-3 text-left transition-colors',
				selected && 'border-primary ring-primary/40 ring-2'
			)}
		>
			{#if option.preview}{@render option.preview()}{/if}
			<span class="flex items-center gap-2 text-sm font-medium">
				{#if option.icon}<option.icon class="size-4" />{/if}
				{option.label}
			</span>
			{#if option.description}<span class="text-muted-foreground text-xs">{option.description}</span>{/if}
		</button>
	{/each}
</div>
