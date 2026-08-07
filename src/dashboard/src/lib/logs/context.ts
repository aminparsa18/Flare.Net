// Generic typed context helper - the svelte-best-practices skill recommends
// `createContext` over raw `setContext`/`getContext` for type safety, but no such helper
// exists in this repo or in bits-ui, so it's authored here. Standard Symbol-keyed wrapper;
// `get()` throws with a clear message rather than silently returning `undefined` if a
// descendant renders outside the expected provider.

import { getContext, hasContext, setContext } from 'svelte';
import type { LogsExplorerState } from './state.svelte';

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

/** +page.svelte calls `.set(new LogsExplorerState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const logsExplorerContext = createContext<LogsExplorerState>('logs-explorer');
