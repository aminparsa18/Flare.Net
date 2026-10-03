// Minimal, safe Markdown -> HTML for the dashboard `Text` panel (roadmap's "Text/Markdown
// dashboard panel" item). Deliberately tiny and dependency-free: every character of input is
// HTML-escaped *before* any markup is emitted, and the only tags ever produced are the fixed
// allow-list below, so raw HTML in the source is shown as literal text and nothing the author
// types can introduce an element, attribute or script. Links are limited to http(s) (and
// always open in a new tab with rel=noopener); anything else stays plain text.
//
// Supported: # headings (1-6), paragraphs, `-`/`*`/`+` and `1.` lists, > blockquotes, ---
// rules, ``` fenced code, and inline **bold**, *italic*/_italic_, `code`, [text](http(s) url).
// No images, tables or nested lists - a notes/runbook-link panel doesn't need them.

function escapeHtml(text: string): string {
	return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/** `url` if it's an absolute http(s) URL, else `null`. Parsed (not prefix-matched) so `javascript:`/`data:` and odd casings can't slip through. */
export function safeHttpUrl(url: string): string | null {
	try {
		const parsed = new URL(url.trim());
		return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.href : null;
	} catch {
		return null;
	}
}

/** Inline formatting on one already-unescaped line of text; returns escaped HTML. */
function renderInline(input: string): string {
	const text = input.replace(/\u0000/g, ''); // NUL marks the placeholders below, so it can't come from the source
	// Code spans first, swapped for placeholders so their contents skip the other rules.
	const codes: string[] = [];
	let work = text.replace(/`([^`]+)`/g, (_, code: string) => {
		codes.push(`<code>${escapeHtml(code)}</code>`);
		return `\u0000${codes.length - 1}\u0000`;
	});
	// Links likewise - the label is escaped here, the href re-checked, then both parked.
	work = work.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (match, label: string, url: string) => {
		const href = safeHttpUrl(url);
		if (!href) return match;
		codes.push(`<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer">${renderEmphasis(escapeHtml(label))}</a>`);
		return `\u0000${codes.length - 1}\u0000`;
	});
	work = renderEmphasis(escapeHtml(work));
	return work.replace(/\u0000(\d+)\u0000/g, (_, i: string) => codes[Number(i)]);
}

/** Bold/italic on already-escaped text. */
function renderEmphasis(escaped: string): string {
	return escaped
		.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
		.replace(/(^|[^*\w])\*([^*\s][^*]*)\*(?!\*)/g, '$1<em>$2</em>')
		.replace(/(^|[^_\w])_([^_\s][^_]*)_(?![_\w])/g, '$1<em>$2</em>');
}

/** Renders `source` Markdown to sanitized HTML (see this file's header for the guarantees). */
export function renderMarkdown(source: string): string {
	const lines = source.replace(/\r\n?/g, '\n').split('\n');
	const out: string[] = [];
	let i = 0;

	while (i < lines.length) {
		const line = lines[i];

		if (line.trim() === '') {
			i++;
			continue;
		}

		if (line.trimStart().startsWith('```')) {
			const code: string[] = [];
			i++;
			while (i < lines.length && !lines[i].trimStart().startsWith('```')) code.push(lines[i++]);
			i++; // closing fence (or end of input)
			out.push(`<pre><code>${escapeHtml(code.join('\n'))}</code></pre>`);
			continue;
		}

		const heading = /^(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line);
		if (heading) {
			const level = heading[1].length;
			out.push(`<h${level}>${renderInline(heading[2])}</h${level}>`);
			i++;
			continue;
		}

		if (/^\s{0,3}([-*_])(\s*\1){2,}\s*$/.test(line)) {
			out.push('<hr>');
			i++;
			continue;
		}

		if (/^\s{0,3}>/.test(line)) {
			const quote: string[] = [];
			while (i < lines.length && /^\s{0,3}>/.test(lines[i])) quote.push(lines[i++].replace(/^\s{0,3}>\s?/, ''));
			out.push(`<blockquote>${renderInline(quote.join(' '))}</blockquote>`);
			continue;
		}

		const listMatch = /^\s{0,3}([-*+]|\d+\.)\s+/.exec(line);
		if (listMatch) {
			const ordered = /\d/.test(listMatch[1]);
			const itemPattern = ordered ? /^\s{0,3}\d+\.\s+(.*)$/ : /^\s{0,3}[-*+]\s+(.*)$/;
			const items: string[] = [];
			while (i < lines.length) {
				const item = itemPattern.exec(lines[i]);
				if (!item) break;
				items.push(`<li>${renderInline(item[1])}</li>`);
				i++;
			}
			const tag = ordered ? 'ol' : 'ul';
			out.push(`<${tag}>${items.join('')}</${tag}>`);
			continue;
		}

		// Paragraph: consecutive lines until a blank line or another block starts.
		const para: string[] = [];
		while (
			i < lines.length &&
			lines[i].trim() !== '' &&
			!lines[i].trimStart().startsWith('```') &&
			!/^#{1,6}\s/.test(lines[i]) &&
			!/^\s{0,3}>/.test(lines[i]) &&
			!/^\s{0,3}([-*+]|\d+\.)\s+/.test(lines[i])
		) {
			para.push(lines[i++]);
		}
		out.push(`<p>${renderInline(para.join('\n')).replace(/\n/g, '<br>')}</p>`);
	}

	return out.join('\n');
}
