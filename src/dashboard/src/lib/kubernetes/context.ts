// Same generic typed-context helper as `$lib/hosts/context.ts` - re-authored here rather
// than shared, same rationale `$lib/services/context.ts` gives.

import { getContext, hasContext, setContext } from 'svelte';
import type { KubernetesState } from './state.svelte';

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

/** `routes/kubernetes/+page.svelte` calls `.set(new KubernetesState())`; every descendant calls `.get()`. */
export const kubernetesContext = createContext<KubernetesState>('kubernetes');
