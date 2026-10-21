const SESSION_KEY = 'flare.session.id';
const SAMPLED_KEY = 'flare.replay.sampled';

/** The subset of `Storage` the recorder uses, so tests can pass a fake. */
export interface KeyValueStore {
	getItem(key: string): string | null;
	setItem(key: string, value: string): void;
}

function safeGet(store: KeyValueStore | undefined, key: string): string | null {
	try {
		return store?.getItem(key) ?? null;
	} catch {
		return null;
	}
}

function safeSet(store: KeyValueStore | undefined, key: string, value: string): void {
	try {
		store?.setItem(key, value);
	} catch {
		// Private mode or blocked storage: the id then lives only as long as the page, which is fine.
	}
}

export function newSessionId(random: () => string = () => crypto.randomUUID()): string {
	return random();
}

/** The id for this browser tab: the caller's, else one kept in `store`, else a new one that is then kept. */
export function resolveSessionId(explicit: string | undefined, store: KeyValueStore | undefined, create = newSessionId): string {
	if (explicit) return explicit;
	const existing = safeGet(store, SESSION_KEY);
	if (existing && /^[0-9a-zA-Z-]{8,64}$/.test(existing)) return existing;
	const id = create();
	safeSet(store, SESSION_KEY, id);
	return id;
}

/** Whether this session is recorded. Decided once and remembered, so reloading the page does not re-roll it. */
export function isSampled(sampleRate: number, store: KeyValueStore | undefined, random: () => number = Math.random): boolean {
	if (sampleRate >= 1) return true;
	if (sampleRate <= 0) return false;
	const remembered = safeGet(store, SAMPLED_KEY);
	if (remembered === '1' || remembered === '0') return remembered === '1';
	const sampled = random() < sampleRate;
	safeSet(store, SAMPLED_KEY, sampled ? '1' : '0');
	return sampled;
}
