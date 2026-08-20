<script lang="ts">
	// Plain monospace block, not syntax-highlighted - matches the rest of the app's code
	// surfaces (TerminalModal, EventDetailSheet's stack traces): no highlighter dependency
	// installed, and these snippets are short enough that color-coding wouldn't earn its
	// keep. Copy button follows EventDetailSheet.svelte's copyException pattern exactly
	// (same 1500ms Copy->Check reset).
	import { Button } from '$lib/components/ui/button';
	import { cn } from '$lib/utils.js';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';

	let { code, label, class: className }: { code: string; label?: string; class?: string } = $props();

	let copied = $state(false);
	let resetTimer: ReturnType<typeof setTimeout> | undefined;

	async function copy(): Promise<void> {
		await navigator.clipboard.writeText(code);
		copied = true;
		clearTimeout(resetTimer);
		resetTimer = setTimeout(() => (copied = false), 1500);
	}
</script>

<div class={cn('bg-muted/40 group relative rounded-md border', className)}>
	{#if label}
		<div class="text-muted-foreground border-b px-3 py-1.5 text-xs font-medium">{label}</div>
	{/if}
	<pre class="overflow-x-auto p-3 pr-10 text-xs leading-relaxed"><code class="font-mono">{code}</code></pre>
	<Button
		variant="ghost"
		size="icon-xs"
		class="text-muted-foreground hover:text-foreground absolute top-2 right-2"
		title="Copy to clipboard"
		onclick={copy}
	>
		{#if copied}
			<CheckIcon />
		{:else}
			<CopyIcon />
		{/if}
	</Button>
</div>
