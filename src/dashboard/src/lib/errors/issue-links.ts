// Builds the prefilled "new issue" URL for an /errors group (ADR-0195, ADR-0273, ADR-0274). Opening it
// needs no token: GitHub, GitLab and Jira show their own new-issue form with the title and body filled
// in, so the user signs in as themselves and files the issue. GitHub and GitLab come from a service's
// source link (ADR-0095); Jira from its issue tracker config.

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

export interface NewIssueLink {
	provider: 'GitHub' | 'GitLab';
	url: string;
}

const titleOf = (draft: IssueDraft): string => `${draft.exceptionType}${draft.exceptionMessage ? `: ${draft.exceptionMessage}` : ''}`.slice(0, 200);

/** The issue's title and body as Markdown (GitHub, GitLab). Used by both the prefilled link and "Create issue". */
export function markdownIssueText(draft: IssueDraft): { title: string; body: string } {
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
	return { title: titleOf(draft), body };
}

/** The issue's title and body as plain/wiki text (Jira has no Markdown); the Flare link stays bare for Jira to auto-link. */
export function plainIssueText(draft: IssueDraft): { title: string; body: string } {
	const body = [
		`Exception: ${draft.exceptionType}`,
		draft.exceptionMessage && `Message: ${draft.exceptionMessage}`,
		`Services: ${draft.services.join(', ')}`,
		`Occurrences: ${draft.occurrenceCount} (first ${draft.firstSeen}, last ${draft.lastSeen})`,
		`Open in Flare: ${draft.flareUrl}`
	]
		.filter(Boolean)
		.join('\n\n')
		.slice(0, MAX_BODY);
	return { title: titleOf(draft), body };
}

/** The new-issue URL for the first affected service whose source link is a GitHub or GitLab repo, or null. */
export function buildNewIssueLink(draft: IssueDraft, configs: readonly SourceLinkConfig[]): NewIssueLink | null {
	const config = draft.services
		.map((s) => configs.find((c) => c.serviceName === s && (c.provider === 'GitHub' || c.provider === 'GitLab')))
		.find((c) => c);
	if (!config) return null;

	const { title, body } = markdownIssueText(draft);
	const repo = config.repoUrl.replace(/\/+$/, '').replace(/\.git$/, '');
	const t = encodeURIComponent(title);
	const b = encodeURIComponent(body);
	// GitLab nests the fields under `issue[...]` and serves the form at `/-/issues/new`.
	return config.provider === 'GitLab'
		? { provider: 'GitLab', url: `${repo}/-/issues/new?issue%5Btitle%5D=${t}&issue%5Bdescription%5D=${b}` }
		: { provider: 'GitHub', url: `${repo}/issues/new?title=${t}&body=${b}` };
}

/** Mirrors `IssueTrackerDto` (Flare.Api/Model/IssueTrackerModels.cs). */
export interface IssueTrackerConfig {
	serviceName: string;
	provider: 'Jira';
	/** Site root, e.g. `https://acme.atlassian.net`. */
	siteUrl: string;
	/** Numeric project id (not the key). */
	projectId: string;
	/** Optional numeric issue type id; empty = Jira's default. Required to create issues from Flare (ADR-0275). */
	issueTypeId: string;
	/** Jira Cloud account email for "Create Jira issue"; empty = a Server/Data Center personal access token. */
	accountEmail?: string;
	/** Write-only: sent on save (omit = keep the stored token, '' = clear), never returned. */
	apiToken?: string;
	/** Response-only: whether an API token is stored. */
	hasApiToken?: boolean;
}

/**
 * The Jira create-issue URL for the first affected service with a tracker config, or null. Jira's
 * `CreateIssueDetails!init.jspa` takes the numeric project id and plain-text fields; the description is
 * wiki text, so it avoids Markdown and leaves the Flare link bare for Jira to auto-link.
 */
export function buildJiraIssueUrl(draft: IssueDraft, trackers: readonly IssueTrackerConfig[]): string | null {
	const config = draft.services.map((s) => trackers.find((t) => t.serviceName === s && t.provider === 'Jira')).find((t) => t);
	if (!config) return null;

	const { title: summary, body: description } = plainIssueText(draft);

	const params = [`pid=${encodeURIComponent(config.projectId)}`];
	if (config.issueTypeId) params.push(`issuetype=${encodeURIComponent(config.issueTypeId)}`);
	params.push(`summary=${encodeURIComponent(summary)}`, `description=${encodeURIComponent(description)}`);
	return `${config.siteUrl.replace(/\/+$/, '')}/secure/CreateIssueDetails!init.jspa?${params.join('&')}`;
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

/** One "Create <tracker> issue" menu entry: which tracker, and the service whose stored config (and write token) it uses. */
export interface CreateIssueOption {
	target: 'GitHub' | 'GitLab' | 'Jira';
	serviceName: string;
}

/**
 * The trackers a group's issue can be created in from Flare (ADR-0275): for each tracker, the first affected service with
 * a write token stored (GitHub/GitLab: issue token on its source link; Jira: API token plus an issue type id on its tracker
 * config). Empty when none is set up, which hides the menu entries.
 */
export function createIssueOptions(
	draft: IssueDraft,
	configs: readonly SourceLinkConfig[],
	trackers: readonly IssueTrackerConfig[]
): CreateIssueOption[] {
	const options: CreateIssueOption[] = [];
	for (const target of ['GitHub', 'GitLab'] as const) {
		const service = draft.services.find((s) => configs.some((c) => c.serviceName === s && c.provider === target && c.hasIssueToken));
		if (service) options.push({ target, serviceName: service });
	}
	const jira = draft.services.find((s) => trackers.some((t) => t.serviceName === s && t.provider === 'Jira' && t.hasApiToken && t.issueTypeId));
	if (jira) options.push({ target: 'Jira', serviceName: jira });
	return options;
}
