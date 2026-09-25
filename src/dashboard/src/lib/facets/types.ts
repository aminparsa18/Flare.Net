// Page-agnostic shape FacetSidebar.svelte renders - the Logs and Traces pages each build
// their own list of these (`$lib/logs/facets.ts`, `$lib/traces/facets.ts`) from their
// explorer state, so the sidebar itself never knows about LogFilter vs SpanFilter.

export interface FacetOption {
	value: string;
	count: number;
}

export interface FacetDefinition {
	/** Stable identity - keys the sidebar's {#each}, so a section keeps its expanded/"show more" state across reloads. */
	id: string;
	title: string;
	/**
	 * Fetches this facet's options, in display order, under the page's current filter
	 * *minus this facet's own selection* - otherwise picking one service would collapse
	 * the Service list to just that service, and a second one could never be added.
	 * Called untracked (see FacetSection.svelte), so reading explorer state here is fine.
	 */
	load: (signal: AbortSignal) => Promise<FacetOption[]>;
	/** Display text for a value - also used for selected values the latest `load` didn't return. Defaults to the raw value. */
	label?: (value: string) => string;
	selected: string[];
	onChange: (next: string[]) => void;
	/** At most one value at a time - Traces' duration buckets, since SpanFilter carries one min/max range. */
	single?: boolean;
	/** Present for user-added attribute facets only - the built-in sections can't be removed. */
	onRemove?: () => void;
}

/** One user-chosen attribute facet - bag + key, same pair an attribute filter targets. */
export interface AttributeFacetRef<TBag extends string> {
	bag: TBag;
	key: string;
}
