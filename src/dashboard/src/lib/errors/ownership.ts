// Pure helpers behind /errors ownership rules (ADR-0195, ADR-0196): a rule maps a service name,
// an exception type or a source-file path to the user who owns errors like it. The first matching
// rule wins; an explicit assignee always beats a rule. No I/O, no Svelte.
// Server-side twin: src/Flare.Api/Errors/ErrorOwnership.cs (alert messages). Keep the two in sync.

export type OwnershipKind = 'Service' | 'ExceptionType' | 'Path';

/** Mirrors `ErrorOwnershipRule` (Flare.Api/Model/ErrorOwnershipModels.cs). */
export interface OwnershipRule {
	kind: OwnershipKind;
	pattern: string;
	owner: string;
}

/** Case-insensitive glob: `*` matches any run of characters (including none), everything else is literal. */
export function globMatches(pattern: string, value: string): boolean {
	const source = pattern
		.split('*')
		.map((part) => part.replace(/[.+?^${}()|[\]\\]/g, '\\$&'))
		.join('.*');
	return new RegExp(`^${source}$`, 'is').test(value);
}

/**
 * CODEOWNERS / gitignore-style path match against a repo-relative path. A pattern with a slash
 * (other than a trailing one) is anchored at the repo root, one without matches at any depth; a
 * trailing `/` means a directory; `*` stays inside a segment, `**` crosses segments, `?` is one
 * character. A pattern naming a directory also owns everything beneath it.
 */
export function codeownersMatches(pattern: string, path: string): boolean {
	let p = pattern.trim().replaceAll('\\', '/');
	if (!p) return false;
	const dirOnly = p.endsWith('/');
	if (dirOnly) p = p.slice(0, -1);
	const anchored = p.startsWith('/') || p.includes('/');
	p = p.replace(/^\/+/, '');
	if (!p) return true; // "/" owns the whole repo

	let source = '';
	for (let i = 0; i < p.length; i++) {
		const c = p[i];
		if (c === '*') {
			if (p[i + 1] === '*') {
				i++;
				if (p[i + 1] === '/') {
					i++;
					source += '(?:.*/)?'; // "**/" = zero or more directories
				} else {
					source += '.*';
				}
			} else {
				source += '[^/]*';
			}
		} else if (c === '?') {
			source += '[^/]';
		} else {
			source += c.replace(/[.+^${}()|[\]\\]/g, '\\$&');
		}
	}
	const re = new RegExp(`^${anchored ? '' : '(?:.*/)?'}${source}(?:/.*)?$`, 'i');
	return re.test(path.replaceAll('\\', '/').replace(/^\/+/, ''));
}

/**
 * Candidate repo-relative paths for a build-machine path. The checkout prefix isn't known here,
 * so every trailing run of segments is tried (`/ci/build/src/pay/Charge.cs` -> `ci/build/src/...`,
 * `build/src/...`, `src/pay/Charge.cs`, ...); a root-anchored pattern then matches the suffix that
 * really starts at the repo root.
 */
export function pathSuffixes(path: string): string[] {
	const segments = path
		.replaceAll('\\', '/')
		.replace(/^[a-z]:/i, '')
		.split('/')
		.filter(Boolean);
	return segments.map((_, i) => segments.slice(i).join('/'));
}

function pathRuleMatches(pattern: string, paths: readonly string[]): boolean {
	return paths.some((p) => pathSuffixes(p).some((candidate) => codeownersMatches(pattern, candidate)));
}

/** The rule that owns a group - the first in list order that matches its service, type or sample paths - or null. */
export function ownerFromRules(
	rules: readonly OwnershipRule[],
	exceptionType: string,
	services: readonly string[],
	paths: readonly string[] = []
): OwnershipRule | null {
	for (const rule of rules) {
		const hit =
			rule.kind === 'Service'
				? services.some((s) => globMatches(rule.pattern, s))
				: rule.kind === 'Path'
					? pathRuleMatches(rule.pattern, paths)
					: globMatches(rule.pattern, exceptionType);
		if (hit) return rule;
	}
	return null;
}

/**
 * Turns a CODEOWNERS file into `Path` rules. CODEOWNERS is last-match-wins while rules here are
 * first-match-wins, so the order is reversed. Only the first owner on a line is used, with a
 * leading `@` dropped (`@org/team` stays `org/team`); sections (`[Name]`), comments and ownerless
 * lines (which un-own a path in GitHub) are skipped.
 */
export function parseCodeowners(text: string): OwnershipRule[] {
	const rules: OwnershipRule[] = [];
	for (const raw of text.split(/\r?\n/)) {
		const line = raw.replace(/(^|\s)#.*$/, '').trim();
		if (!line || line.startsWith('[') || line.startsWith('^[')) continue;
		const parts = line.split(/\s+/);
		if (parts.length < 2) continue;
		const owner = parts[1].replace(/^@/, '');
		if (owner) rules.push({ kind: 'Path', pattern: parts[0], owner });
	}
	return rules.reverse();
}
