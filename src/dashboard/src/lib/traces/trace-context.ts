// Same generic typed-context helper as `./context.ts` (the list page's own) - a
// separate context key/instance since the waterfall is a separate route with its own
// state class (`TraceDetailState`).

import { getContext, hasContext, setContext } from 'svelte';
import type { TraceDetailState } from './trace-state.svelte';

export function createContext<T>(name: string) {
	const key = Symbol(name);
	return {
		set: (value: T): T => setContext(key, value),
		get: (): T => {
			if (!hasContext(key)) {
				throw new Error(`No context found for "${name}" - did an ancestor component forget to call .set()?`);
			}
			return getContext<T>(key);
		}
	};
}

/** `routes/traces/[traceId]/+page.svelte` calls `.set(new TraceDetailState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const traceDetailContext = createContext<TraceDetailState>('trace-detail');
