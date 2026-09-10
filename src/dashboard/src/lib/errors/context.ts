// Same generic typed-context helper as `$lib/traces/context.ts`/`$lib/logs/context.ts` -
// re-authored here rather than shared, same rationale those two files already give (no
// natural shared-utils home for a two-line helper yet).

import { getContext, hasContext, setContext } from 'svelte';
import type { ErrorsExplorerState } from './state.svelte';

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

/** `routes/errors/+page.svelte` calls `.set(new ErrorsExplorerState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const errorsExplorerContext = createContext<ErrorsExplorerState>('errors-explorer');
