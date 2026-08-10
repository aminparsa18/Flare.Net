// Same generic typed-context helper as $lib/alerts/context.ts/$lib/logs/context.ts -
// re-authored here rather than shared, per those files' own note: no such helper exists
// in bits-ui, and this repo doesn't have a shared-utils module for a two-line helper.

import { getContext, hasContext, setContext } from 'svelte';
import type { UsersState } from './state.svelte';

function createContext<T>(name: string) {
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

/** `routes/users/+page.svelte` calls `.set(new UsersState())`; UserTable.svelte calls `.get()` instead of receiving it as a prop. */
export const usersContext = createContext<UsersState>('users');
