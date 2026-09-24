<script lang="ts">
	import { ansiStyleToCss, hasAnsi, parseAnsi } from '$lib/logs/ansi';

	// Renders a log body with its ANSI SGR colors as styled spans - text nodes only, never
	// {@html}, so a body can't inject markup. A body with no ESC byte (the common case)
	// renders as a bare text node with no parsing or extra elements, which matters inside
	// the virtualized Logs table.
	let { text }: { text: string | null | undefined } = $props();

	const segments = $derived(hasAnsi(text) ? parseAnsi(text!) : null);
</script>

{#if segments}{#each segments as segment, i (i)}{@const css = ansiStyleToCss(segment.style)}{#if css}<span style={css}
			>{segment.text}</span
		>{:else}{segment.text}{/if}{/each}{:else}{text ?? ''}{/if}
