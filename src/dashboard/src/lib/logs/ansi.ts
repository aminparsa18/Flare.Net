// ANSI escape-sequence handling for log bodies. Console-formatted output (colored ASP.NET
// Core console logger, CLI tools, anything that wrote to a TTY before its stdout was
// shipped over OTLP) arrives with raw `ESC[31m...ESC[0m` SGR sequences in the body.
//
// This never produces HTML - it splits the text into plain-string segments plus a style
// descriptor, and the caller (AnsiText.svelte) renders each segment as a text node inside
// a <span style=...>, so Svelte's own escaping still applies to every character of the
// body. Only SGR (`ESC[...m`) is interpreted; every other CSI/OSC/two-byte escape (cursor
// movement, erase-line, window titles, hyperlinks) is stripped, since none of them mean
// anything in a table cell.

export interface AnsiStyle {
	/** CSS color value, or undefined for the inherited foreground. */
	fg?: string;
	/** CSS color value, or undefined for no background. */
	bg?: string;
	bold?: boolean;
	dim?: boolean;
	italic?: boolean;
	underline?: boolean;
	strikethrough?: boolean;
	inverse?: boolean;
}

export interface AnsiSegment {
	text: string;
	style: AnsiStyle;
}

const ESC = '\x1b';

// CSI (ESC [ params intermediates final), OSC (ESC ] ... terminated by BEL or ESC \), and
// any other two-byte ESC sequence (ESC 7, ESC c, ...). CSI params are 0x30-0x3F,
// intermediates 0x20-0x2F, final byte 0x40-0x7E. A body truncated mid-sequence (a CSI
// with no final byte before end-of-text, or a lone trailing ESC) is dropped too.
// eslint-disable-next-line no-control-regex
const ESCAPE_RE = /\x1b\[([0-?]*)[ -/]*([@-~])|\x1b\[[0-?]*[ -/]*$|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)?|\x1b[@-Z\\-_]?/g;

/** Cheap pre-check so the (overwhelmingly common) plain body skips parsing entirely. */
export function hasAnsi(text: string | null | undefined): boolean {
	return !!text && text.includes(ESC);
}

/** The body with every escape sequence removed - for tooltips, copy-to-clipboard, etc. */
export function stripAnsi(text: string): string {
	return hasAnsi(text) ? text.replace(ESCAPE_RE, '') : text;
}

// The 16 base colors resolve to theme tokens (--ansi-0..--ansi-15 in routes/layout.css)
// so e.g. "black" text stays readable in dark mode instead of being hard-coded #000.
function baseColor(index: number): string {
	return `var(--ansi-${index})`;
}

// xterm 256-color palette: 0-15 base, 16-231 a 6x6x6 cube, 232-255 a grayscale ramp.
function color256(n: number): string | undefined {
	if (!Number.isInteger(n) || n < 0 || n > 255) return undefined;
	if (n < 16) return baseColor(n);
	if (n < 232) {
		const c = n - 16;
		const level = (v: number) => (v === 0 ? 0 : 55 + v * 40);
		return `rgb(${level(Math.floor(c / 36))}, ${level(Math.floor(c / 6) % 6)}, ${level(c % 6)})`;
	}
	const gray = 8 + (n - 232) * 10;
	return `rgb(${gray}, ${gray}, ${gray})`;
}

function isByte(n: number): boolean {
	return Number.isInteger(n) && n >= 0 && n <= 255;
}

// Reads an extended color (38/48 ;5;n or ;2;r;g;b) starting at params[i] (the 5 or 2).
// Returns the color and how many params it consumed; a malformed one consumes the rest.
function extendedColor(params: number[], i: number): { color?: string; consumed: number } {
	const mode = params[i];
	if (mode === 5) {
		return { color: color256(params[i + 1]), consumed: 2 };
	}
	if (mode === 2) {
		const [r, g, b] = [params[i + 1], params[i + 2], params[i + 3]];
		return { color: isByte(r) && isByte(g) && isByte(b) ? `rgb(${r}, ${g}, ${b})` : undefined, consumed: 4 };
	}
	return { consumed: params.length - i };
}

function applySgr(style: AnsiStyle, raw: string): AnsiStyle {
	// "ESC[m" is a reset, same as "ESC[0m". Colon sub-parameters (38:2::r:g:b) are
	// normalised to the semicolon form - both appear in the wild.
	const params = raw === '' ? [0] : raw.split(/[;:]/).map((p) => (p === '' ? 0 : Number(p)));
	let next: AnsiStyle = { ...style };

	for (let i = 0; i < params.length; i++) {
		const p = params[i];
		if (p === 0) next = {};
		else if (p === 1) next.bold = true;
		else if (p === 2) next.dim = true;
		else if (p === 3) next.italic = true;
		else if (p === 4) next.underline = true;
		else if (p === 7) next.inverse = true;
		else if (p === 9) next.strikethrough = true;
		else if (p === 22) next.bold = next.dim = false;
		else if (p === 23) next.italic = false;
		else if (p === 24) next.underline = false;
		else if (p === 27) next.inverse = false;
		else if (p === 29) next.strikethrough = false;
		else if (p >= 30 && p <= 37) next.fg = baseColor(p - 30);
		else if (p === 39) next.fg = undefined;
		else if (p >= 40 && p <= 47) next.bg = baseColor(p - 40);
		else if (p === 49) next.bg = undefined;
		else if (p >= 90 && p <= 97) next.fg = baseColor(p - 90 + 8);
		else if (p >= 100 && p <= 107) next.bg = baseColor(p - 100 + 8);
		else if (p === 38 || p === 48) {
			const { color, consumed } = extendedColor(params, i + 1);
			if (p === 38) next.fg = color;
			else next.bg = color;
			i += consumed;
		}
		// Anything else (blink, fonts, overline, ...) is ignored rather than rejected.
	}
	return next;
}

/**
 * Splits text into styled segments. Adjacent text under the same style is merged, and
 * empty segments are never emitted. Never throws on malformed input.
 */
export function parseAnsi(text: string): AnsiSegment[] {
	if (!hasAnsi(text)) return text ? [{ text, style: {} }] : [];

	const segments: AnsiSegment[] = [];
	let style: AnsiStyle = {};
	let last = 0;

	const push = (chunk: string) => {
		if (!chunk) return;
		const prev = segments[segments.length - 1];
		if (prev && prev.style === style) prev.text += chunk;
		else segments.push({ text: chunk, style });
	};

	for (const match of text.matchAll(ESCAPE_RE)) {
		push(text.slice(last, match.index));
		last = match.index + match[0].length;
		// Only CSI with final byte 'm' (SGR) changes style; everything else is dropped.
		if (match[2] === 'm' && match[0][1] === '[') style = applySgr(style, match[1]);
	}
	push(text.slice(last));
	return segments;
}

/** Inline CSS for one segment's style. */
export function ansiStyleToCss(style: AnsiStyle): string {
	let fg = style.fg;
	// Backgrounds are tinted rather than solid: a terminal's "black background" (which
	// ASP.NET Core's console logger puts behind every level tag) is invisible on a black
	// terminal, and a translucent mix reproduces that on either theme instead of drawing
	// an opaque gray box. Inverse video is the exception - it's meant to be a solid block.
	let bg = style.bg && `color-mix(in srgb, ${style.bg} 25%, transparent)`;
	if (style.inverse) {
		[fg, bg] = [style.bg ?? 'var(--background)', style.fg ?? 'var(--foreground)'];
	}

	const css: string[] = [];
	if (fg) css.push(`color: ${fg}`);
	if (bg) css.push(`background-color: ${bg}`);
	if (style.bold) css.push('font-weight: 600');
	if (style.dim) css.push('opacity: 0.7');
	if (style.italic) css.push('font-style: italic');
	const decorations = [style.underline && 'underline', style.strikethrough && 'line-through'].filter(Boolean);
	if (decorations.length) css.push(`text-decoration-line: ${decorations.join(' ')}`);
	return css.join('; ');
}
