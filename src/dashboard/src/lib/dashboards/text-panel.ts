// Shape of a `Text` dashboard panel's `query` (roadmap's "Text/Markdown dashboard panel" item).
// A Text panel has no data source, so `query` just carries the Markdown source; it's rendered
// by `renderMarkdown` (`$lib/dashboards/markdown.ts`), which never emits raw HTML.

export interface TextPanelQuery {
	markdown: string;
}

/** Max Markdown length a Text panel accepts - keeps the layout blob (and every dashboard PUT) bounded. */
export const TEXT_PANEL_MAX_LENGTH = 10_000;

/** A Text panel's Markdown source, tolerating a missing/malformed `query` (unknown at rest, same posture as every other panel's saved query). */
export function textPanelMarkdown(query: unknown): string {
	const value = (query as Partial<TextPanelQuery> | null | undefined)?.markdown;
	return typeof value === 'string' ? value : '';
}
