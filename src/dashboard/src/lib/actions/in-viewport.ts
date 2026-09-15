// Svelte action: fires `onEnter` once, the first time `node` scrolls within `rootMargin`
// of the viewport, then stops observing - a "has this ever been visible" signal, not a
// continuous enter/exit toggle. Built for DashboardPanelCard.svelte's lazy panel
// rendering (roadmap's "lazy-loading panels - only fetch/render what's in viewport, not
// every panel on page load", signoz#2133): a dashboard with many panels shouldn't fire
// every one's query on page load, only the ones actually scrolled into view, with the
// rest loading as the user scrolls to them. Fire-once (rather than unmounting again on
// scroll-out) deliberately doesn't re-fetch/discard a panel repeatedly as it scrolls past
// - that's virtualization, a different, much larger change this roadmap item never asked
// for; once a panel has paid for its first query, keeping it mounted is strictly cheaper.
//
// `root: null` (the default) uses the browser viewport, not an explicit scroll-container
// ref - browsers already clip the intersection rectangle by every intermediate
// `overflow: auto` ancestor (here, the dashboard viewer's own scrolling wrapper) even
// when root is the top-level viewport, so this needs no wiring back to that ancestor.
const PRELOAD_MARGIN = '200px'; // start loading slightly before the panel is actually on-screen, so scrolling to it doesn't show a bare spinner first

export function inViewport(node: Element, onEnter: () => void) {
	// IntersectionObserver is missing only in environments this app doesn't ship to
	// (very old browsers, some test/SSR shells) - fail open rather than leaving a panel
	// permanently unrendered.
	if (typeof IntersectionObserver === 'undefined') {
		onEnter();
		return { destroy() {} };
	}

	const observer = new IntersectionObserver(
		(entries) => {
			if (entries.some((entry) => entry.isIntersecting)) {
				onEnter();
				observer.disconnect();
			}
		},
		{ rootMargin: PRELOAD_MARGIN }
	);
	observer.observe(node);

	return {
		destroy() {
			observer.disconnect();
		}
	};
}
