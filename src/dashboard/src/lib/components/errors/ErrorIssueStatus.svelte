<script lang="ts">
	// The Status cell of the exception-groups table: the group's triage state (ADR-0121) as a
	// badge, plus its assignee and what an ignore / regression is waiting on. A group nobody has
	// triaged reads Open.
	import { Badge } from '$lib/components/ui/badge';
	import type { ErrorIssue } from '$lib/error-issues-api';
	import * as m from '$lib/paraglide/messages';
	import { formatDateTime } from '$lib/time/format';

	let { issue }: { issue: ErrorIssue | null } = $props();

	const status = $derived(issue?.status ?? 'Open');
	const variant = $derived(status === 'Regressed' ? 'destructive' : status === 'Resolved' ? 'default' : status === 'Ignored' ? 'secondary' : 'outline');
	const label = $derived(
		status === 'Resolved'
			? m.errorIssue_statusResolved()
			: status === 'Ignored'
				? m.errorIssue_statusIgnored()
				: status === 'Regressed'
					? m.errorIssue_statusRegressed()
					: m.errorIssue_statusOpen()
	);

	const detail = $derived.by(() => {
		if (!issue) return '';
		if (issue.status === 'Regressed') return issue.regressedVersion ? m.errorIssue_regressedIn({ version: issue.regressedVersion }) : '';
		if (issue.status !== 'Ignored') return '';
		const parts: string[] = [];
		if (issue.ignoreUntil) parts.push(m.errorIssue_ignoredUntil({ when: formatDateTime(issue.ignoreUntil) }));
		if (issue.ignoreUntilOccurrences != null)
			parts.push(m.errorIssue_ignoredCount({ seen: issue.occurrencesSinceChange, limit: issue.ignoreUntilOccurrences }));
		return parts.join(' · ');
	});
</script>

<div class="flex flex-col items-start gap-0.5">
	<Badge {variant}>{label}</Badge>
	{#if detail}
		<span class="text-muted-foreground text-[0.625rem]">{detail}</span>
	{/if}
	{#if issue?.assignee}
		<span class="text-muted-foreground text-[0.625rem]">{m.errorIssue_assignedTo({ user: issue.assignee })}</span>
	{/if}
</div>
