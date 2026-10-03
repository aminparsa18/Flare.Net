<script lang="ts">
	// Renders a "Text" panel: its Markdown (with `$variable` references filled in, same
	// substitution a panel title gets) as sanitized HTML. No query, so no refresh/lazy-load
	// handling - DashboardPanelCard mounts it immediately. `renderMarkdown` escapes all input
	// and emits only a fixed tag allow-list, which is what makes {@html} safe here.
	import { renderMarkdown } from '$lib/dashboards/markdown';
	import { textPanelMarkdown } from '$lib/dashboards/text-panel';
	import { resolvePanelTitle } from '$lib/dashboards/variables';
	import type { DashboardVariable } from '$lib/dashboards-api';
	import * as m from '$lib/paraglide/messages';

	let {
		query,
		variables,
		variableValues
	}: {
		query: unknown;
		variables: DashboardVariable[];
		variableValues: Record<string, string[]>;
	} = $props();

	const source = $derived(resolvePanelTitle(textPanelMarkdown(query), variables, variableValues, m.dashboardViewer_variableAll()));
	const html = $derived(renderMarkdown(source));
</script>

{#if source.trim() === ''}
	<div class="text-muted-foreground flex h-full items-center justify-center p-3 text-sm">{m.dashboardTextPanel_empty()}</div>
{:else}
	<div class="markdown-body h-full overflow-auto p-3 text-sm">
		<!-- eslint-disable-next-line svelte/no-at-html-tags -->
		{@html html}
	</div>
{/if}

<style>
	.markdown-body :global(h1) { font-size: 1.25rem; font-weight: 600; margin: 0 0 0.5rem; }
	.markdown-body :global(h2) { font-size: 1.125rem; font-weight: 600; margin: 0.75rem 0 0.5rem; }
	.markdown-body :global(h3),
	.markdown-body :global(h4),
	.markdown-body :global(h5),
	.markdown-body :global(h6) { font-size: 1rem; font-weight: 600; margin: 0.75rem 0 0.25rem; }
	.markdown-body :global(p) { margin: 0 0 0.5rem; }
	.markdown-body :global(ul) { list-style: disc; padding-left: 1.25rem; margin: 0 0 0.5rem; }
	.markdown-body :global(ol) { list-style: decimal; padding-left: 1.25rem; margin: 0 0 0.5rem; }
	.markdown-body :global(a) { color: var(--primary); text-decoration: underline; }
	.markdown-body :global(code) { background: var(--muted); border-radius: 0.25rem; padding: 0.05rem 0.3rem; font-size: 0.85em; }
	.markdown-body :global(pre) { background: var(--muted); border-radius: 0.375rem; padding: 0.5rem 0.75rem; margin: 0 0 0.5rem; overflow-x: auto; }
	.markdown-body :global(pre code) { background: none; padding: 0; }
	.markdown-body :global(blockquote) { border-left: 3px solid var(--border); padding-left: 0.75rem; color: var(--muted-foreground); margin: 0 0 0.5rem; }
	.markdown-body :global(hr) { border-color: var(--border); margin: 0.75rem 0; }
</style>
