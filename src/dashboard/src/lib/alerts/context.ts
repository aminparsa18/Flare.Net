// Same generic typed-context helper as `$lib/logs/context.ts` - re-authored here rather
// than shared, since that file's own comment notes no such helper exists in bits-ui and
// this repo doesn't currently have a place both features would import a third copy from
// without introducing a new "shared utils" module for a two-line helper.

import { getContext, hasContext, setContext } from 'svelte';
import type { AlertsState } from './state.svelte';

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

/** `routes/alerts/+page.svelte` calls `.set(new AlertsState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const alertsContext = createContext<AlertsState>('alerts');
