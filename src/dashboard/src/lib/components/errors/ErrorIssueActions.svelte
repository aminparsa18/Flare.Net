<script lang="ts">
	// Row menu of the exception-groups table: resolve / ignore (forever, for a while, or for N
	// more occurrences) / reopen / assign - one `PUT /api/errors/issues` each (ADR-0121). Only
	// rendered for users who can mutate; the API enforces Member/Admin independently.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import { Button } from '$lib/components/ui/button';
	import EllipsisIcon from '@lucide/svelte/icons/ellipsis';
	import { errorsExplorerContext } from '$lib/errors/context';
	import { authContext } from '$lib/auth/context';
	import type { ExceptionGroup } from '$lib/errors-api';
	import * as m from '$lib/paraglide/messages';

	let { group }: { group: ExceptionGroup } = $props();

	const errors = errorsExplorerContext.get();
	const auth = authContext.get();

	const status = $derived(errors.statusOf(group));
	const assignee = $derived(errors.issueFor(group)?.assignee ?? '');
	// With auth off there is no current user to assign to.
	const me = $derived(auth.currentUser?.username ?? '');

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
