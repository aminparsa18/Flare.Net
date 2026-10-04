// Server-side per-user preference documents (ADR-0110): GET/PUT /api/me/preferences/{key}.
// Shared by the appearance and regional stores. localStorage stays each store's pre-paint
// cache; this only moves the document to and from the server, best-effort.
import { API_BASE_URL, apiFetch } from '$lib/api';

const PUSH_DELAY_MS = 500;

export interface PrefsSync {
	/** The stored document; null when the server has none; undefined when unreachable/unsupported. */
	pull(): Promise<Record<string, unknown> | null | undefined>;
	/** Debounced PUT of whatever `snapshot()` returns at send time. */
	schedulePush(snapshot: () => unknown): void;
	/** Immediate PUT; resolves when the request settles (never rejects). */
	pushNow(snapshot: unknown): Promise<void>;
}

export function createPrefsSync(key: string): PrefsSync {
	const url = `${API_BASE_URL}/api/me/preferences/${key}`;
	let timer: ReturnType<typeof setTimeout> | undefined;

	async function pushNow(snapshot: unknown): Promise<void> {
		try {
			await apiFetch(url, {
				method: 'PUT',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(snapshot)
			});
		} catch {
			// Best-effort; the next change retries.
		}
	}

	return {
		async pull() {
			try {
				const res = await apiFetch(url);
				if (res.status === 204) return null;
				if (!res.ok) return undefined;
				return (await res.json()) as Record<string, unknown>;
			} catch {
				return undefined;
			}
		},
		schedulePush(snapshot) {
			clearTimeout(timer);
			timer = setTimeout(() => void pushNow(snapshot()), PUSH_DELAY_MS);
		},
		pushNow
	};
}
