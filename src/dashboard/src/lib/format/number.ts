// The one place numbers are formatted for display. Follows Settings > Regional > Number format
// ($lib/regional): "auto" keeps the browser's own locale (what `toLocaleString()` did before), the
// others pin a separator style. Formatters are cached per locale + options, and `formatNumber`
// reads the setting on every call, so templates/$derived re-render when it changes.
import { regional } from '$lib/regional/prefs.svelte';

const cache = new Map<string, Intl.NumberFormat>();

/** The locale tag to format with; undefined = the browser's. */
export function numberLocale(): string | undefined {
	return regional.numberFormat === 'auto' ? undefined : regional.numberFormat;
}

export function numberFormat(options?: Intl.NumberFormatOptions): Intl.NumberFormat {
	const locale = numberLocale();
	const key = `${locale ?? ''}|${options ? JSON.stringify(options) : ''}`;
	let f = cache.get(key);
	if (!f) {
		f = new Intl.NumberFormat(locale, options);
		cache.set(key, f);
	}
	return f;
}

export function formatNumber(value: number, options?: Intl.NumberFormatOptions): string {
	return numberFormat(options).format(value);
}

/** Drop-in for a module-scope `new Intl.NumberFormat(undefined, options)` that must follow the setting. */
export function lazyNumberFormat(options?: Intl.NumberFormatOptions): { format(value: number): string } {
	return { format: (value) => formatNumber(value, options) };
}
