// Shared parsing for the repeatable --attr/--attr-not/--attr-exists/--attr-absent flags
// commands/search.ts/traces.ts both expose - mirrors Flare.Cli's own
// Internal/AttributeFlagParsing.cs so the CLI and this dashboard-terminal port stay
// interchangeable. Always targets the filter's default/primary attribute bag
// (LogAttributes/SpanAttributes) - no --attr-bag flag yet, same scope the roadmap item
// that added this shipped with. Throws a plain Error (not either file's own UsageError
// subclass) on a malformed flag - both callers' run() already display any thrown Error's
// message the same way, so no shared error type is needed here.

export interface AttrFlagEntry {
	key: string;
	value: string;
	operator: 'Equals' | 'NotEquals' | 'Exists' | 'Absent';
}

/** Parses `KEY=VALUE` for --attr (Equals)/--attr-not (NotEquals). */
export function parseAttrKeyValue(raw: string, flag: string, commandName: string, operator: 'Equals' | 'NotEquals'): AttrFlagEntry {
	const separator = raw.indexOf('=');
	if (separator <= 0) throw new Error(`${commandName}: ${flag} expects KEY=VALUE, got '${raw}'`);
	return { key: raw.slice(0, separator), value: raw.slice(separator + 1), operator };
}

/** Parses a bare KEY for --attr-exists (Exists)/--attr-absent (Absent). */
export function parseAttrBareKey(raw: string, flag: string, commandName: string, operator: 'Exists' | 'Absent'): AttrFlagEntry {
	if (!raw.trim()) throw new Error(`${commandName}: ${flag} expects a non-empty KEY`);
	return { key: raw, value: '', operator };
}
