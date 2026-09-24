// Same generic typed-context helper as `$lib/saved-views/context.ts` - re-authored here
// rather than shared, per that file's own comment on why (no shared "context utils"
// module yet for a two-line helper).

import { getContext, hasContext, setContext } from 'svelte';
import type { IngestKeysState } from './state.svelte';

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

/** `routes/ingest-keys/+page.svelte` calls `.set(new IngestKeysState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const ingestKeysContext = createContext<IngestKeysState>('ingest-keys');
