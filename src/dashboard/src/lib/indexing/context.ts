// Same generic typed-context helper as `$lib/ingestion/context.ts`/`$lib/metrics/context.ts` -
// re-authored here rather than shared, same rationale those already give (no natural
// shared-utils home for a two-line helper yet).

import { getContext, hasContext, setContext } from 'svelte';
import type { IndexingState } from './state.svelte';

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

/** `routes/indexing/+page.svelte` calls `.set(new IndexingState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const indexingContext = createContext<IndexingState>('indexing');
