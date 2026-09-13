// Shared signal letting a child route hide the app's nav chrome (AppNav/CommandPalette,
// rendered in routes/+layout.svelte) - e.g. the dashboard viewer's full-screen/TV mode
// (routes/dashboards/[id]/+page.svelte). No route needed cross-route chrome control before
// this, so there's no existing precedent to follow here - kept to a single boolean rather
// than a richer "chrome configuration" object since hiding the nav is the only thing any
// route needs to do with it today.
//
// Context can only be set during an ancestor's own component initialization, so the root
// layout - not this module - constructs ChromeVisibilityState and calls
// setChromeVisibilityContext(); any descendant route reads it back with
// getChromeVisibilityContext() and flips `.hidden` itself. Same generic
// getContext/setContext-with-a-Symbol-key shape as `$lib/dashboards/context.ts`'s
// createContext - re-authored here rather than shared, same rationale that file gives (no
// natural shared-utils home for a two-line helper yet).

import { getContext, setContext } from 'svelte';

const KEY = Symbol('chrome-visibility');

export class ChromeVisibilityState {
	hidden = $state(false);
}

/** Called once, by routes/+layout.svelte, during its own initialization. */
export function setChromeVisibilityContext(value: ChromeVisibilityState): ChromeVisibilityState {
	return setContext(KEY, value);
}

/** Called by any descendant route that needs to hide the nav while it's active. */
export function getChromeVisibilityContext(): ChromeVisibilityState {
	return getContext<ChromeVisibilityState>(KEY);
}
