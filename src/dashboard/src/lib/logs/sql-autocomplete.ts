// Context-aware autocomplete for the SQL query row - so a user who doesn't already know
// the grammar can discover what's available (keywords, columns, functions) as they type,
// rather than needing to read docs first. Walks the tokenized text up to the cursor with
// a small explicit state machine mirroring (not sharing code with - it's TS, the grammar
// lives in Flare.Api's Query/LogQl/LogQlParser.cs) the server's actual grammar, so the
// suggestions offered always match what the parser will accept. Forgiving on
// malformed/partial input by design (this is a hint, not a validator) - anything the walk
// can't confidently place just yields no suggestions rather than guessing wrong; the
// server-side parser is still the real authority when the query actually runs.

import { tokenizeLogQl, type HighlightToken } from './sql-highlight';

export interface LogQlSuggestion {
	/** Shown in the dropdown. */
	label: string;
	/** Replaces the in-progress word (see `LogQlSuggestionResult.wordStart`) when accepted. */
	insertText: string;
	detail?: string;
}

export interface LogQlSuggestionResult {
	/** Offset where the in-progress word starts - equals `cursorPos` when there isn't one. */
	wordStart: number;
	suggestions: LogQlSuggestion[];
}

const COLUMN_NAMES = ['Service', 'Level', 'Body', 'TraceId', 'SpanId'];
const NUMERIC_COLUMN_NAMES = ['SeverityNumber'];
const ALL_COLUMN_NAMES = [...COLUMN_NAMES, ...NUMERIC_COLUMN_NAMES];

const OPERATOR_SUGGESTIONS: LogQlSuggestion[] = [
	{ label: '=', insertText: '= ' },
	{ label: '!=', insertText: '!= ' },
	{ label: '<', insertText: '< ' },
	{ label: '<=', insertText: '<= ' },
	{ label: '>', insertText: '> ' },
	{ label: '>=', insertText: '>= ' },
	{ label: 'like', insertText: "like '", detail: "e.g. like '%text%'" },
	{ label: 'not like', insertText: "not like '", detail: "e.g. not like '%text%'" }
];

const DURATION_SUGGESTIONS = ['5m', '15m', '1h', '6h', '24h', '7d'];

/** Every phase the walk can land in - see `walkPhase` for the transitions between them. */
type Phase =
	| 'select'
	| 'select-list'
	| 'select-list-done'
	| 'awaiting-count-paren'
	| 'awaiting-avg-paren'
	| 'awaiting-sum-paren'
	| 'agg-arg-star'
	| 'agg-arg-column'
	| 'stream'
	| 'after-stream'
	| 'where-column'
	| 'where-op'
	| 'where-value'
	| 'after-value'
	| 'group'
	| 'group-by'
	| 'awaiting-time-paren'
	| 'time-arg'
	| 'after-time'
	| 'group-secondary';

/** What kind of `(` is currently open, so a matching `)` knows which phase to land in. */
type ParenContext = 'count' | 'avg' | 'sum' | 'time' | 'where-group';

export function getLogQlSuggestions(text: string, cursorPos: number): LogQlSuggestionResult {
	// The word still being typed (if any) right before the cursor is a filter prefix, not
	// part of the "what came before" context walk.
	const before = text.slice(0, cursorPos);
	const wordMatch = /[A-Za-z_][A-Za-z0-9_]*$/.exec(before);
	const wordStart = wordMatch ? cursorPos - wordMatch[0].length : cursorPos;
	const currentWord = wordMatch ? wordMatch[0] : '';

	const contextTokens = tokenizeLogQl(text.slice(0, wordStart)).filter((t) => t.text.trim().length > 0);
	const phase = walkPhase(contextTokens);

	const suggestions = suggestionsFor(phase).filter((s) =>
		currentWord ? s.label.toLowerCase().startsWith(currentWord.toLowerCase()) : true
	);

	return { wordStart, suggestions };
}

function walkPhase(tokens: HighlightToken[]): Phase {
	let phase: Phase = 'select';
	const parens: ParenContext[] = [];

	for (const token of tokens) {
		const lower = token.text.toLowerCase();
		const top = parens[parens.length - 1];

		if (token.text === '(') {
			if (phase === 'awaiting-count-paren') {
				parens.push('count');
				phase = 'agg-arg-star';
			} else if (phase === 'awaiting-avg-paren') {
				parens.push('avg');
				phase = 'agg-arg-column';
			} else if (phase === 'awaiting-sum-paren') {
				parens.push('sum');
				phase = 'agg-arg-column';
			} else if (phase === 'awaiting-time-paren') {
				parens.push('time');
				phase = 'time-arg';
			} else if (phase === 'where-column') {
				// A grouping paren around a boolean subexpression - still expecting a
				// column (or 'not'/another paren) right after it.
				parens.push('where-group');
			}
			continue;
		}

		if (token.text === ')') {
			const popped = parens.pop();
			if (popped === 'count' || popped === 'avg' || popped === 'sum') {
				phase = 'select-list-done';
			} else if (popped === 'time') {
				phase = 'after-time';
			} else if (popped === 'where-group') {
				phase = 'after-value';
			}
			continue;
		}

		if (token.text === ',') {
			if (top === undefined && phase === 'select-list-done') {
				phase = 'select-list';
			} else if (top === 'time') {
				phase = 'group-secondary';
			}
			continue;
		}

		switch (phase) {
			case 'select':
				if (lower === 'select') phase = 'select-list';
				break;
			case 'select-list':
				if (lower === '*' || ALL_COLUMN_NAMES.some((c) => c.toLowerCase() === lower)) {
					phase = 'select-list-done';
				} else if (lower === 'count') {
					phase = 'awaiting-count-paren';
				} else if (lower === 'avg') {
					phase = 'awaiting-avg-paren';
				} else if (lower === 'sum') {
					phase = 'awaiting-sum-paren';
				}
				break;
			case 'select-list-done':
				if (lower === 'from') phase = 'stream';
				break;
			case 'stream':
				if (lower === 'stream') phase = 'after-stream';
				break;
			case 'after-stream':
				if (lower === 'where') phase = 'where-column';
				else if (lower === 'group') phase = 'group';
				break;
			case 'where-column':
				if (COLUMN_NAMES.some((c) => c.toLowerCase() === lower)) phase = 'where-op';
				break;
			case 'where-op':
				if (lower === 'like' || token.type === 'operator') phase = 'where-value';
				break;
			case 'where-value':
				if (token.type === 'string') phase = 'after-value';
				break;
			case 'after-value':
				if (lower === 'and' || lower === 'or') phase = 'where-column';
				else if (lower === 'group') phase = 'group';
				break;
			case 'group':
				if (lower === 'by') phase = 'group-by';
				break;
			case 'group-by':
				if (lower === 'time') phase = 'awaiting-time-paren';
				break;
			// agg-arg-star / agg-arg-column / time-arg / after-time / group-secondary / the
			// 'awaiting-*-paren' phases all just wait for their matching '(' / ')' / ',',
			// already handled above - nothing to advance on any other token.
			default:
				break;
		}
	}

	return phase;
}

function suggestionsFor(phase: Phase): LogQlSuggestion[] {
	switch (phase) {
		case 'select':
			return [{ label: 'select', insertText: 'select ', detail: 'start a query' }];
		case 'select-list':
			return [
				{ label: '*', insertText: '* ', detail: 'every column' },
				{ label: 'count(*)', insertText: 'count(*) ', detail: 'a total count' },
				{ label: 'avg(...)', insertText: 'avg(SeverityNumber) ', detail: 'average of a numeric column' },
				{ label: 'sum(...)', insertText: 'sum(SeverityNumber) ', detail: 'sum of a numeric column' },
				...ALL_COLUMN_NAMES.map((c) => ({ label: c, insertText: `${c} `, detail: 'column' }))
			];
		case 'agg-arg-star':
			return [{ label: '*', insertText: '*)', detail: 'every matching row' }];
		case 'agg-arg-column':
			return NUMERIC_COLUMN_NAMES.map((c) => ({ label: c, insertText: `${c})`, detail: 'numeric column' }));
		case 'select-list-done':
			return [{ label: 'from', insertText: 'from ', detail: 'e.g. from stream' }];
		case 'stream':
			return [{ label: 'stream', insertText: 'stream ', detail: 'the log event source' }];
		case 'after-stream':
			return [
				{ label: 'where', insertText: 'where ', detail: 'filter rows' },
				{ label: 'group by', insertText: 'group by ', detail: 'bucket by time' }
			];
		case 'where-column':
			return COLUMN_NAMES.map((c) => ({ label: c, insertText: `${c} `, detail: 'column' }));
		case 'where-op':
			return OPERATOR_SUGGESTIONS;
		case 'after-value':
			return [
				{ label: 'and', insertText: 'and ', detail: '' },
				{ label: 'or', insertText: 'or ', detail: '' },
				{ label: 'group by', insertText: 'group by ', detail: 'bucket by time' }
			];
		case 'group':
			return [{ label: 'by', insertText: 'by ', detail: '' }];
		case 'group-by':
			return [{ label: 'time(...)', insertText: 'time(1h)', detail: 'bucket width' }];
		case 'time-arg':
			return DURATION_SUGGESTIONS.map((d) => ({ label: d, insertText: `${d})`, detail: '' }));
		case 'after-time':
			return [
				{ label: ', service', insertText: ', service', detail: 'split by service' },
				{ label: ', level', insertText: ', level', detail: 'split by level' }
			];
		case 'group-secondary':
			return [
				{ label: 'service', insertText: 'service', detail: '' },
				{ label: 'level', insertText: 'level', detail: '' }
			];
		default:
			return [];
	}
}
