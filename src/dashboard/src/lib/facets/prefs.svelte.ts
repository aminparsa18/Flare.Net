// Per-browser facet sidebar preferences (open/closed + which attribute facets are shown),
// one instance per page. localStorage for the same reason `$lib/logs/pinned-attributes`
// gives: there's no server-side user settings store, and a layout preference like this
// doesn't warrant one. Deliberately not part of a saved view - a view reproduces *what is
// filtered*, not how the sidebar is arranged.
import { browser } from '$app/environment';
import type { AttributeFacetRef } from './types';

interface StoredPrefs {
	open?: unknown;
	attributes?: unknown;
}

export class FacetSidebarPrefs<TBag extends string> {
	open = $state(true);
	attributes = $state<AttributeFacetRef<TBag>[]>([]);

	#storageKey: string;

	/**
	 * @param defaults Attribute facets shown before the user has changed anything (Logs'
	 * environment/host) - removable like any other, and not re-added once removed.
	 * @param bags Whitelist a stored entry's `bag` is checked against, so a corrupt or
	 * foreign value can't reach a request.
	 */
	constructor(storageKey: string, defaults: AttributeFacetRef<TBag>[], bags: readonly TBag[]) {
		this.#storageKey = storageKey;
		this.attributes = defaults;
		if (!browser) return;
		try {
			const raw = localStorage.getItem(storageKey);
			if (!raw) return;
			const stored = JSON.parse(raw) as StoredPrefs;
			if (typeof stored.open === 'boolean') this.open = stored.open;
			if (Array.isArray(stored.attributes)) {
				this.attributes = stored.attributes.filter(
					(a): a is AttributeFacetRef<TBag> =>
						a != null && typeof a.key === 'string' && a.key !== '' && (bags as readonly unknown[]).includes(a.bag)
				);
			}
		} catch {
			// Corrupt value or storage disabled - keep the defaults.
		}
	}

	setOpen(open: boolean): void {
		this.open = open;
		this.#save();
	}

	addAttribute(bag: TBag, key: string): void {
		const trimmed = key.trim();
		if (!trimmed || this.attributes.some((a) => a.bag === bag && a.key === trimmed)) return;
		this.attributes = [...this.attributes, { bag, key: trimmed }];
		this.#save();
	}

	removeAttribute(bag: TBag, key: string): void {
		this.attributes = this.attributes.filter((a) => !(a.bag === bag && a.key === key));
		this.#save();
	}

	#save(): void {
		if (!browser) return;
		try {
			localStorage.setItem(this.#storageKey, JSON.stringify({ open: this.open, attributes: this.attributes }));
		} catch {
			// Storage full/disabled - the change still applies for this session.
		}
	}
}
