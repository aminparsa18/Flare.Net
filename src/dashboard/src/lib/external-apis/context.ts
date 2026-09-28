// Same generic typed-context helper as `$lib/messaging/context.ts` - re-authored here rather
// than shared, same rationale that file gives.

import { getContext, hasContext, setContext } from 'svelte';
import type { ExternalApisState } from './state.svelte';

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

/** `routes/external-apis/+page.svelte` calls `.set(new ExternalApisState())`; every descendant calls `.get()`. */
export const externalApisContext = createContext<ExternalApisState>('external-apis');
