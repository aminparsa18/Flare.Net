<script lang="ts">
	// Row menu of the exception-groups table: resolve / ignore (forever, for a while, or for N
	// more occurrences) / reopen / assign - one `PUT /api/errors/issues` each (ADR-0121). Only
	// rendered for users who can mutate; the API enforces Member/Admin independently.
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import EllipsisIcon from '@lucide/svelte/icons/ellipsis';
	import { errorsExplorerContext } from '#lib/errors/context.ts';
	import { authContext } from '#lib/auth/context.ts';
	import LinkIssueDialog from './LinkIssueDialog.svelte';
	import MergeGroupDialog from './MergeGroupDialog.svelte';
	import { buildJiraIssueUrl, buildNewIssueLink, createIssueOptions, markdownIssueText, plainIssueText, type CreateIssueOption } from '#lib/errors/issue-links.ts';
	import { issueKey } from '#lib/error-issues-api.ts';
	import type { ExceptionGroup } from '#lib/errors-api.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { group }: { group: ExceptionGroup } = $props();

	const errors = errorsExplorerContext.get();
	const auth = authContext.get();

	const status = $derived(errors.statusOf(group));
	const assignee = $derived(errors.issueFor(group)?.assignee ?? '');
	// With auth off there is no current user to assign to.
	const me = $derived(auth.currentUser?.username ?? '');

	const issueUrl = $derived(errors.issueFor(group)?.issueUrl ?? '');
	let linkOpen = $state(false);
	let mergeOpen = $state(false);

	// What both prefilled new-issue links carry about the group (ADR-0195, ADR-0274).
	const draft = $derived({
		exceptionType: group.exceptionType,
		exceptionMessage: group.exceptionMessage,
		services: group.affectedServices,
		occurrenceCount: Number(group.occurrenceCount),
		firstSeen: group.firstSeen,
		lastSeen: group.lastSeen,
		flareUrl: new URL(errors.groupHref(group) ?? '/errors', window.location.origin).toString()
	});
	// The prefilled new-issue page of the first affected service whose source repo is on GitHub or GitLab (ADR-0195).
	const newIssue = $derived(buildNewIssueLink(draft, errors.sourceLinks));
	// The prefilled Jira create page of the first affected service with a Jira tracker configured (ADR-0274).
	const newJiraIssueUrl = $derived(buildJiraIssueUrl(draft, errors.issueTrackers));

	// "Create issue" entries (ADR-0275): trackers with a write token stored, only while the group has no linked issue.
	const createOptions = $derived(issueUrl ? [] : createIssueOptions(draft, errors.sourceLinks, errors.issueTrackers));
	const creating = $derived(errors.creatingIssue.has(issueKey(group.exceptionType, group.exceptionMessage)));
	const createLabel = (option: CreateIssueOption) =>
		option.target === 'GitLab' ? m.errorIssue_createGitLabIssue() : option.target === 'Jira' ? m.errorIssue_createJiraIssue() : m.errorIssue_createGitHubIssue();
	const createIssue = (option: CreateIssueOption) => errors.createIssue(group, option, option.target === 'Jira' ? plainIssueText(draft) : markdownIssueText(draft));

	const DAY_MS = 24 * 60 * 60 * 1000;
	const ignoreUntil = (days: number) => new Date(Date.now() + days * DAY_MS).toISOString();
</script>

<DropdownMenu.Root>
	<DropdownMenu.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="ghost" size="icon-sm" aria-label={m.errorIssue_actions()} onclick={(e: MouseEvent) => e.stopPropagation()}>
				<EllipsisIcon />
			</Button>
		{/snippet}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content align="end" class="w-56" onclick={(e: MouseEvent) => e.stopPropagation()}>
		{#if status === 'Resolved' || status === 'Regressed' || status === 'Ignored'}
			<DropdownMenu.Item onclick={() => errors.updateIssue(group, { status: 'Open' })}>{m.errorIssue_reopen()}</DropdownMenu.Item>
		{/if}
		{#if status !== 'Resolved'}
			<DropdownMenu.Item onclick={() => errors.updateIssue(group, { status: 'Resolved' })}>{m.errorIssue_resolve()}</DropdownMenu.Item>
		{/if}
		<DropdownMenu.Separator />
		<DropdownMenu.Label>{m.errorIssue_ignoreHeading()}</DropdownMenu.Label>
		<DropdownMenu.Item onclick={() => errors.updateIssue(group, { status: 'Ignored' })}>{m.errorIssue_ignoreForever()}</DropdownMenu.Item>
		<DropdownMenu.Item onclick={() => errors.updateIssue(group, { status: 'Ignored', ignoreUntil: ignoreUntil(1) })}>{m.errorIssue_ignore24h()}</DropdownMenu.Item>
		<DropdownMenu.Item onclick={() => errors.updateIssue(group, { status: 'Ignored', ignoreUntil: ignoreUntil(7) })}>{m.errorIssue_ignore7d()}</DropdownMenu.Item>
		<DropdownMenu.Item onclick={() => errors.updateIssue(group, { status: 'Ignored', ignoreUntilOccurrences: 100 })}>{m.errorIssue_ignore100()}</DropdownMenu.Item>
		<DropdownMenu.Separator />
		{#if newIssue}
			<DropdownMenu.Item onclick={() => window.open(newIssue.url, '_blank', 'noopener,noreferrer')}>
				{newIssue.provider === 'GitLab' ? m.errorIssue_newGitLabIssue() : m.errorIssue_newGitHubIssue()}
			</DropdownMenu.Item>
		{/if}
		{#if newJiraIssueUrl}
			<DropdownMenu.Item onclick={() => window.open(newJiraIssueUrl, '_blank', 'noopener,noreferrer')}>{m.errorIssue_newJiraIssue()}</DropdownMenu.Item>
		{/if}
		{#each createOptions as option (option.target)}
			<DropdownMenu.Item disabled={creating} onclick={() => createIssue(option)}>{creating ? m.errorIssue_creating() : createLabel(option)}</DropdownMenu.Item>
		{/each}
		<DropdownMenu.Item onclick={() => (linkOpen = true)}>{issueUrl ? m.errorIssue_changeLink() : m.errorIssue_linkIssue()}</DropdownMenu.Item>
		{#if issueUrl}
			<DropdownMenu.Item onclick={() => errors.updateIssue(group, { issueUrl: '' })}>{m.errorIssue_removeLink()}</DropdownMenu.Item>
		{/if}
		<DropdownMenu.Item onclick={() => (mergeOpen = true)}>{m.errorGrouping_mergeMenu()}</DropdownMenu.Item>
		{#if me || assignee}
			<DropdownMenu.Separator />
			{#if me && assignee !== me}
				<DropdownMenu.Item onclick={() => errors.updateIssue(group, { assignee: me })}>{m.errorIssue_assignToMe()}</DropdownMenu.Item>
			{/if}
			{#if assignee}
				<DropdownMenu.Item onclick={() => errors.updateIssue(group, { assignee: '' })}>{m.errorIssue_unassign()}</DropdownMenu.Item>
			{/if}
		{/if}
	</DropdownMenu.Content>
</DropdownMenu.Root>

<MergeGroupDialog bind:open={mergeOpen} {group} />
<LinkIssueDialog bind:open={linkOpen} current={issueUrl} onSave={(url) => errors.updateIssue(group, { issueUrl: url })} />
