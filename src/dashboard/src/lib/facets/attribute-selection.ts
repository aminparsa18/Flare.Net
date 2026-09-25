// How an attribute facet's checkbox selection maps onto a page's ordinary attribute
// filters - shared by Logs (AttributeFilter) and Traces (SpanAttributeFilter), whose
// shapes match. A facet selection *is* an `In` filter on its bag + key (a single pick is
// still `In`, one value), so it shows up in, and stays editable from, the page's existing
// attribute filters row, and a saved view needs no new field to carry it. An `Equals`
// filter on the same key (EventDetailSheet's "filter for value") counts as selected too.
// Any other operator on that key (NotEquals, Regex, ...) is left alone, both here and in
// the facet's own count query.

interface AttributeFilterLike<TBag extends string> {
	bag: TBag;
	key: string;
	value: string;
	operator?: string;
	values?: string[];
}

function isSelection<TBag extends string>(f: AttributeFilterLike<TBag>, bag: TBag, key: string): boolean {
	const operator = f.operator ?? 'Equals';
	return f.bag === bag && f.key === key && (operator === 'Equals' || operator === 'In');
}

export function selectedAttributeValues<TBag extends string>(filters: AttributeFilterLike<TBag>[], bag: TBag, key: string): string[] {
	const values = filters
		.filter((f) => isSelection(f, bag, key))
		.flatMap((f) => ((f.operator ?? 'Equals') === 'In' ? (f.values ?? []) : [f.value]));
	return [...new Set(values)];
}

export function withoutAttributeSelection<T extends AttributeFilterLike<TBag>, TBag extends string>(filters: T[], bag: TBag, key: string): T[] {
	return filters.filter((f) => !isSelection(f, bag, key));
}

export function withAttributeSelection<T extends AttributeFilterLike<TBag>, TBag extends string>(
	filters: T[],
	bag: TBag,
	key: string,
	values: string[]
): T[] {
	const rest = withoutAttributeSelection(filters, bag, key);
	if (values.length === 0) return rest;
	return [...rest, { bag, key, value: '', operator: 'In', values } as T];
}
