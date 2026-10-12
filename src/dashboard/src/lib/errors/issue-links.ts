// Builds the prefilled "new GitHub issue" URL for an /errors group (ADR-0195). Opening it needs no
// token: GitHub shows its own new-issue form with the title and body filled in, so the user signs
// in as themselves and files the issue. Only GitHub repos configured as a service's source link
// (ADR-0095) qualify.

import type { SourceLinkConfig } from './source-links';

/** GitHub caps a prefilled URL around 8 KB; keep the body well under so the browser never truncates it. */
const MAX_BODY = 4000;

export interface IssueDraft {
	exceptionType: string;
	exceptionMessage: string;
	services: readonly string[];
	occurrenceCount: number;
	firstSeen: string;
	lastSeen: string;
	/** Absolute URL of the group on /errors, so the issue links back. */
	flareUrl: string;
}

/** The new-issue URL for the first affected service whose source link is a GitHub repo, or null. */
export function buildGitHubIssueUrl(draft: IssueDraft, configs: readonly SourceLinkConfig[]): string | null {
	const config = draft.services.map((s) => configs.find((c) => c.serviceName === s && c.provider === 'GitHub')).find((c) => c);
	if (!config) return null;

	const title = `${draft.exceptionType}${draft.exceptionMessage ? `: ${draft.exceptionMessage}` : ''}`.slice(0, 200);
	const body = [
		`**Exception:** \`${draft.exceptionType}\``,
		draft.exceptionMessage && `**Message:** ${draft.exceptionMessage}`,
		`**Services:** ${draft.services.join(', ')}`,
		`**Occurrences:** ${draft.occurrenceCount} (first ${draft.firstSeen}, last ${draft.lastSeen})`,
		`[Open in Flare](${draft.flareUrl})`
	]
		.filter(Boolean)
		.join('\n\n')
		.slice(0, MAX_BODY);

	const repo = config.repoUrl.replace(/\/+$/, '').replace(/\.git$/, '');
	return `${repo}/issues/new?title=${encodeURIComponent(title)}&body=${encodeURIComponent(body)}`;
}

/** Short label for a linked issue URL: `#42` for a GitHub/GitLab issue, else the host. */
export function issueLinkLabel(url: string): string {
	try {
		const u = new URL(url);
		const num = /\/issues\/(\d+)\/?$/.exec(u.pathname)?.[1];
		return num ? `#${num}` : u.host;
	} catch {
		return url;
	}
}
