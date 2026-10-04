// "Did you mean..." for empty results caused by letter case. Attribute `Equals`/`In`
// filters match exactly, so `level=warn` against data that says `Warn` silently returns
// nothing. When a search comes back empty, `findCaseSuggestions` asks the existing
// attribute-values endpoint (case-insensitive substring `prefix`) for values that differ
// from the typed one only by case, counted with every *other* filter still applied.
// Shared by the Logs and Traces explorers - both filter shapes satisfy `CaseFilterLike`.

export interface CaseFilterLike {
	key: string;
	value: string;
	operator?: string;
	values?: string[];
}

export interface CaseSuggestion {
	/** Index into the user-built attribute filters the suggestion would rewrite. */
	filterIndex: number;
	key: string;
	typed: string;
	actual: string;
	count: number;
}

/** Operands a filter matches exactly - empty for every other operator (nothing to case-fold). */
export function exactOperands(f: CaseFilterLike): string[] {
	const op = f.operator ?? 'Equals';
	if (op === 'Equals') return f.value ? [f.value] : [];
	if (op === 'In') return (f.values ?? []).filter((v) => v !== '');
	return [];
}

/** Candidates differing from `typed` only by case, most-observed first. */
export function caseVariants(typed: string, candidates: { value: string; count: number }[]): { value: string; count: number }[] {
	const lower = typed.toLowerCase();
	return candidates
		.filter((c) => c.value !== typed && c.value.toLowerCase() === lower)
		.sort((a, b) => b.count - a.count);
}

const MAX_FILTERS = 3;
const MAX_OPERANDS_PER_FILTER = 5;
const MAX_SUGGESTIONS = 5;
const CANDIDATE_LIMIT = 50;

/**
 * @param fetchValues Returns observed values for `filters[index]`'s key, narrowed by `prefix`,
 * with every filter *except* that one applied (otherwise the typed-wrong filter itself
 * would zero the counts).
 */
export async function findCaseSuggestions<F extends CaseFilterLike>(
	filters: F[],
	fetchValues: (index: number, prefix: string, limit: number) => Promise<{ value: string; count: number }[]>
): Promise<CaseSuggestion[]> {
	const jobs: Promise<CaseSuggestion[]>[] = [];
	let considered = 0;
	for (let i = 0; i < filters.length && considered < MAX_FILTERS; i++) {
		const operands = exactOperands(filters[i]).slice(0, MAX_OPERANDS_PER_FILTER);
		if (operands.length === 0) continue;
		considered++;
		for (const typed of operands) {
			jobs.push(
				fetchValues(i, typed, CANDIDATE_LIMIT).then((candidates) =>
					caseVariants(typed, candidates).map((c) => ({ filterIndex: i, key: filters[i].key, typed, actual: c.value, count: c.count }))
				)
			);
		}
	}
	const settled = await Promise.allSettled(jobs);
	return settled
		.flatMap((r) => (r.status === 'fulfilled' ? r.value : []))
		.sort((a, b) => b.count - a.count)
		.slice(0, MAX_SUGGESTIONS);
}

/** Next filter list with `s.typed` swapped for `s.actual` in the targeted filter. */
export function applyCaseSuggestion<F extends CaseFilterLike>(filters: F[], s: CaseSuggestion): F[] {
	return filters.map((f, i) => {
		if (i !== s.filterIndex) return f;
		if ((f.operator ?? 'Equals') === 'In') {
			return { ...f, values: (f.values ?? []).map((v) => (v === s.typed ? s.actual : v)) };
		}
		return { ...f, value: s.actual };
	});
}
