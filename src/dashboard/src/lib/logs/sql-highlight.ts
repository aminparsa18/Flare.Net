// Lightweight client-side tokenizer for the SQL query row's syntax highlighting - a
// visual approximation of Flare.Api's LogQl grammar (Query/LogQl/LogQlLexer.cs /
// LogQlParser.cs), not a second source of truth for validity. Deliberately forgiving
// (never throws; anything it doesn't recognize is classified 'plain' rather than
// rejected) since this only feeds an editor overlay - the backend's own parser
// (runLogQlQuery in api.ts) is still what actually validates the query text.

export type LogQlTokenType = 'keyword' | 'function' | 'column' | 'string' | 'duration' | 'operator' | 'plain';

export interface HighlightToken {
	text: string;
	type: LogQlTokenType;
}

// select/from/where/group/by/and/or/not/like, plus 'stream' (the grammar's one fixed
// FROM target) - see LogQlParser.Parse's own keyword set.
const KEYWORDS = new Set(['select', 'from', 'where', 'group', 'by', 'and', 'or', 'not', 'like', 'stream']);
// count(*)/avg(col)/sum(col) and time(...) - see LogQlParser's SELECT-list and GROUP BY handling.
const FUNCTIONS = new Set(['count', 'avg', 'sum', 'time']);
// The column allowlist (select list, avg()/sum() argument, where clause) - see
// LogQlAst.LogQlColumn / LogQlParser.ResolveColumn. severitynumber is select/aggregate-only
// (LogQlParser rejects it in a where clause) - this tokenizer doesn't enforce that, same
// "visual approximation, not a second source of truth" reasoning as the rest of this file.
const COLUMNS = new Set(['service', 'level', 'severity', 'body', 'traceid', 'spanid', 'severitynumber']);

// One alternative per token shape, tried left-to-right at each position (matching
// LogQlLexer.cs's own precedence: strings, then a digit-led duration run, then a
// word, then the multi-char comparison operators before the single-char ones so
// "!=" isn't cut short into "!" + "="). Unmatched runs (whitespace, stray characters)
// are re-emitted verbatim as 'plain' by tokenizeLogQl below, so the highlighted
// output is always character-for-character identical to the input - required for the
// overlay-textarea technique (SqlQueryRow.svelte) where this render sits pixel-aligned
// behind the real, transparent-text textarea.
const TOKEN_RE = /'(?:[^']|'')*'?|\b\d+[a-zA-Z]+\b|[A-Za-z_][A-Za-z0-9_]*|!=|<>|<=|>=|[=<>(),*]/g;

function classify(token: string): LogQlTokenType {
	if (token.startsWith("'")) return 'string';
	if (/^\d/.test(token)) return 'duration';
	if (/^(?:!=|<>|<=|>=|[=<>(),*])$/.test(token)) return 'operator';
	const lower = token.toLowerCase();
	if (KEYWORDS.has(lower)) return 'keyword';
	if (FUNCTIONS.has(lower)) return 'function';
	if (COLUMNS.has(lower)) return 'column';
	return 'plain';
}

export function tokenizeLogQl(text: string): HighlightToken[] {
	const tokens: HighlightToken[] = [];
	let lastIndex = 0;
	for (const match of text.matchAll(TOKEN_RE)) {
		const index = match.index ?? 0;
		if (index > lastIndex) {
			tokens.push({ text: text.slice(lastIndex, index), type: 'plain' });
		}
		tokens.push({ text: match[0], type: classify(match[0]) });
		lastIndex = index + match[0].length;
	}
	if (lastIndex < text.length) {
		tokens.push({ text: text.slice(lastIndex), type: 'plain' });
	}
	return tokens;
}

// var(--chart-N) - the same theme-aware categorical palette MetricChart/IngestionChart/
// IndexingGrowthChart already use (see layout.css's chart-1..5 comment), applied via
// inline style rather than a `text-chart-N` Tailwind class: VolumeChart's own remarks
// document that this project's Tailwind build doesn't reliably emit generated color
// utilities for these tokens (confirmed there for fill-*/stroke-*), and every existing
// --chart-N usage in this codebase already goes through var(...) directly rather than a
// class - this follows that same proven pattern instead of gambling on a new one.
export const TOKEN_COLOR_VARS: Partial<Record<LogQlTokenType, string>> = {
	keyword: 'var(--chart-1)',
	function: 'var(--chart-4)',
	string: 'var(--chart-3)',
	duration: 'var(--chart-2)',
	column: 'var(--chart-5)'
	// operator/plain: no entry - both render in the default (inherited) text color, same
	// as a real SQL editor leaving punctuation and unrecognized identifiers uncolored.
};
