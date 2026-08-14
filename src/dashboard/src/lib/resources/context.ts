// Generic typed context helper - duplicated here rather than shared, same convention as
// every other lib/<feature>/context.ts in this repo (see lib/logs/context.ts's identical
// comment: no shared helper exists in this repo or in bits-ui, by design/convention).
// Standard Symbol-keyed wrapper; `get()` throws with a clear message rather than silently
// returning `undefined` if a descendant renders outside the expected provider.

import { getContext, hasContext, setContext } from 'svelte';
import type { ResourcesState } from './state.svelte';

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

/** +page.svelte calls `.set(new ResourcesState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const resourcesContext = createContext<ResourcesState>('resources');
