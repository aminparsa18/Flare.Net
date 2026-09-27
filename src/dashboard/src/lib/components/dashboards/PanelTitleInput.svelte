<script lang="ts">
	// DashboardPanelCard's click-to-edit title field, plus `$`-triggered suggestions of the
	// dashboard's variables - picking one inserts its `$name`/`${name}` reference (see
	// `resolvePanelTitle` in $lib/dashboards/variables.ts) at the caret. Same hand-rolled
	// dropdown and mousedown-before-blur trick AttributeValueCombobox.svelte documents, except
	// `position: fixed` rather than `absolute`: the panel sits inside gridstack's
	// `overflow: hidden` item, which would clip an absolutely positioned list.
	import { tick } from 'svelte';
	import { Input } from '$lib/components/ui/input';
	import { cn } from '$lib/utils';
	import { titleReferenceAtCaret, variableReference } from '$lib/dashboards/variables';
	import type { DashboardVariable } from '$lib/dashboards-api';
	import * as m from '$lib/paraglide/messages';

	let {
		value = $bindable(),
		variables,
		onCommit,
		onCancel
	}: {
		value: string;
		variables: DashboardVariable[];
		onCommit: () => void;
		onCancel: () => void;
	} = $props();

	let input = $state<HTMLInputElement | null>(null);
	/** The reference being typed, or `null` when the caret isn't inside one. */
	let reference = $state<{ start: number; query: string } | null>(null);
	let highlighted = $state(0);
	let position = $state({ top: 0, left: 0 });

	const matches = $derived.by(() => {
		if (!reference) return [];
		const needle = reference.query.toLowerCase();
		return variables.filter((v) => v.name.toLowerCase().includes(needle));
	});
	const open = $derived(matches.length > 0);

	function refresh(): void {
		if (!input || !variables.length) {
			reference = null;
			return;
		}
		const next = titleReferenceAtCaret(input.value, input.selectionStart ?? input.value.length);
		if (next?.query !== reference?.query || next?.start !== reference?.start) highlighted = 0;
		reference = next;
		if (next) {
			const rect = input.getBoundingClientRect();
			position = { top: rect.bottom + 4, left: rect.left };
		}
	}

	async function pick(variable: DashboardVariable): Promise<void> {
		if (!input || !reference) return;
		const caret = input.selectionStart ?? value.length;
		// Also swallows the rest of a half-typed `${...}` after the caret, so picking inside
		// one doesn't leave a stray closing brace behind.
		const after = value.slice(caret);
		const rest = value[reference.start + 1] === '{' && after.startsWith('}') ? after.slice(1) : after;
		const inserted = variableReference(variable);
		value = value.slice(0, reference.start) + inserted + rest;
		reference = null;
		await tick();
		const nextCaret = value.length - rest.length;
		input.setSelectionRange(nextCaret, nextCaret);
		input.focus();
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (open) {
			if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
				e.preventDefault();
				const step = e.key === 'ArrowDown' ? 1 : -1;
				highlighted = (highlighted + step + matches.length) % matches.length;
				return;
			}
			if (e.key === 'Enter' || e.key === 'Tab') {
				e.preventDefault();
				void pick(matches[highlighted]);
				return;
			}
			if (e.key === 'Escape') {
				// Only closes the list - a second Escape cancels the rename.
				e.preventDefault();
				reference = null;
				return;
			}
		}
		if (e.key === 'Enter') onCommit();
		else if (e.key === 'Escape') onCancel();
	}
</script>

<Input
	bind:ref={input}
	class="h-7 text-sm"
	autofocus
	autocomplete="off"
	bind:value
	aria-label={m.dashboardPanelCard_renameLabel()}
	title={variables.length ? m.dashboardPanelCard_titleVariablesHint() : undefined}
	onblur={onCommit}
	oninput={refresh}
	onclick={refresh}
	onkeyup={(e) => {
		if (e.key === 'ArrowLeft' || e.key === 'ArrowRight' || e.key === 'Home' || e.key === 'End') refresh();
	}}
	onkeydown={handleKeydown}
/>
{#if open}
	<div
		class="bg-popover text-popover-foreground ring-foreground/10 fixed z-50 max-h-48 w-56 overflow-auto rounded-md py-1 text-xs shadow-md ring-1"
		style:top="{position.top}px"
		style:left="{position.left}px"
		role="listbox"
	>
		{#each matches as variable, i (variable.id)}
			<button
				type="button"
				role="option"
				aria-selected={i === highlighted}
				class={cn('flex w-full items-center justify-between gap-2 px-2 py-1.5 text-left', i === highlighted ? 'bg-accent text-accent-foreground' : 'hover:bg-accent')}
				onmousedown={(e) => {
					e.preventDefault();
					void pick(variable);
				}}
			>
				<span class="truncate">{variable.name}</span>
				<span class="text-muted-foreground truncate font-mono">{variableReference(variable)}</span>
			</button>
		{/each}
	</div>
{/if}
