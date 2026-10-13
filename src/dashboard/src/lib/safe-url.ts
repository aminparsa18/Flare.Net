// Defence-in-depth URL guards for values that originate from telemetry or ingest clients. The CSP is the
// primary backstop; these keep the same values harmless if it is off (FLARE_CSP=off) or stripped by a proxy.
import { safeHttpUrl } from '$lib/dashboards/markdown';

export { safeHttpUrl };

const IMAGE_TYPES = new Set(['image/png', 'image/jpeg', 'image/webp']);
const BASE64 = /^[A-Za-z0-9+/]+={0,2}$/;

/** A `data:` URL for a session-replay screenshot, or `null` unless it is a PNG/JPEG/WebP with plain base64 content. */
export function safeImageDataUrl(contentType: string, imageBase64: string): string | null {
	const type = contentType.trim().toLowerCase();
	return IMAGE_TYPES.has(type) && BASE64.test(imageBase64) ? `data:${type};base64,${imageBase64}` : null;
}

/** `mailto:<address>` for a bare email address, or `null`. Rejects anything that could add `?cc=`/`?body=` or extra recipients. */
export function safeMailto(email: string): string | null {
	const address = email.trim();
	return /^[^\s@?&,;<>"'%]+@[^\s@?&,;<>"'%]+\.[^\s@?&,;<>"'%]+$/.test(address) ? `mailto:${address}` : null;
}

/** An admin-set support link: absolute http(s), or a `mailto:` address. Anything else (e.g. `javascript:`) is dropped. */
export function safeSupportUrl(url: string): string | null {
	const trimmed = url.trim();
	if (/^mailto:/i.test(trimmed)) return safeMailto(trimmed.slice('mailto:'.length));
	return safeHttpUrl(trimmed);
}
