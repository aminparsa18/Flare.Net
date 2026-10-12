// Maps the Host of a request to the status page published on it (ADR-0165). Server-only: the dashboard asks the
// API, which serves GET /api/public/status/domain/{host} from one cached map of every domain.
import { env as privateEnv } from '$env/dynamic/private';
import { env as publicEnv } from '$env/dynamic/public';

const TTL_MS = 30_000;
// Hosts come from the request, so an attacker can make up as many as they like; cap what we remember.
const MAX_ENTRIES = 500;
const cache = new Map<string, { slug: string | null; expires: number }>();

/** The slug of the status page published on `host`, or null when it is not a status page domain (or the API cannot be reached). */
export async function slugForHost(host: string, fetchFn: typeof fetch = fetch): Promise<string | null> {
	const key = host.toLowerCase();
	const hit = cache.get(key);
	if (hit && hit.expires > Date.now()) return hit.slug;

	// API_INTERNAL_URL is for deployments where the dashboard reaches the API at a different address than browsers do.
	const base = privateEnv.API_INTERNAL_URL || publicEnv.PUBLIC_API_URL || 'http://localhost:5085';
	let slug: string | null = null;
	try {
		const res = await fetchFn(`${base.replace(/\/$/, '')}/api/public/status/domain/${encodeURIComponent(key)}`, {
			headers: { Accept: 'application/json' },
			signal: AbortSignal.timeout(3000)
		});
		if (res.ok) slug = ((await res.json()) as { slug?: string }).slug ?? null;
	} catch {
		// API unreachable: treat as "not a status domain" for this window rather than failing every request.
	}

	if (cache.size >= MAX_ENTRIES) cache.clear();
	cache.set(key, { slug, expires: Date.now() + TTL_MS });
	return slug;
}

/** What a status-page-only host may serve: its own page, the subscription pages, and the app's static assets. */
export function allowedOnStatusDomain(pathname: string, slug: string): boolean {
	return (
		pathname === `/status/${slug}` ||
		pathname.startsWith('/subscribe/') ||
		pathname.startsWith('/_app/') ||
		pathname === '/favicon.ico' ||
		pathname === '/favicon.svg' ||
		pathname === '/robots.txt'
	);
}
