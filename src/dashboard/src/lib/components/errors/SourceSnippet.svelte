<script lang="ts">
	// Inline source for an exception's throw-site frame (ADR-0096). Fetched on demand - a click, not
	// on render - because the dialog lists up to 50 occurrences and each fetch is a round trip to the
	// repo host. Failures show the API's reason (typically "private repo, add a token").
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { getSourceSnippet, type SourceSnippet } from '$lib/source-links-api';
	import type { FrameTarget } from '$lib/errors/source-links';
	import * as m from '$lib/paraglide/messages';

	let { serviceName, target }: { serviceName: string; target: FrameTarget } = $props();

	let snippet = $state<SourceSnippet | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	async function load(): Promise<void> {
		loading = true;
		error = null;
		try {
			snippet = await getSourceSnippet({ serviceName, ref: target.ref.ref, isCommit: target.ref.isCommit, path: target.path, line: target.line });
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}
</script>

<div class="mt-2 text-xs">
	{#if snippet}
		<p class="text-muted-foreground mb-1 font-mono">{target.path}:{target.line}</p>
		<pre class="bg-muted overflow-auto rounded-md border py-2 font-mono leading-5">{#each snippet.lines as text, i (i)}{@const n = snippet.startLine + i}<span class="block px-3 whitespace-pre {n === target.line ? 'bg-destructive/15 font-medium' : ''}"><span class="text-muted-foreground mr-3 inline-block w-8 text-right select-none">{n}</span>{text}</span>{/each}</pre>
	{:else}
		<Button variant="outline" size="sm" onclick={load} disabled={loading}>
			{#if loading}<Spinner class="mr-1 size-3" />{/if}
			{m.sourceSnippet_show({ location: `${target.path}:${target.line}` })}
		</Button>
		{#if error}
			<p class="text-destructive mt-1">{error}</p>
		{/if}
	{/if}
</div>
