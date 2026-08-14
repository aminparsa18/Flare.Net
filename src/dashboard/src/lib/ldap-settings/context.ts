// Same generic typed-context helper as $lib/entra-settings/context.ts - re-authored here
// rather than shared, per that file's own note.

import { getContext, hasContext, setContext } from 'svelte';
import type { LdapSettingsState } from './state.svelte';

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

/** `routes/auth/+page.svelte` calls `.set(new LdapSettingsState())`; `LdapSecurityForm.svelte` calls `.get()` instead of receiving it as a prop. */
export const ldapSettingsContext = createContext<LdapSettingsState>('ldap-settings');
