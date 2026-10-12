// Pure helpers behind /errors ownership rules (ADR-0195): a rule maps a service name or an
// exception type, matched by a case-insensitive glob, to the user who owns errors like it. The
// first matching rule wins; an explicit assignee always beats a rule. No I/O, no Svelte.

export type OwnershipKind = 'Service' | 'ExceptionType';

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

/** The owner of a group by the first matching rule, or null. */
export function ownerFromRules(rules: readonly OwnershipRule[], exceptionType: string, services: readonly string[]): OwnershipRule | null {
	for (const rule of rules) {
		const hit = rule.kind === 'Service' ? services.some((s) => globMatches(rule.pattern, s)) : globMatches(rule.pattern, exceptionType);
		if (hit) return rule;
	}
	return null;
}
