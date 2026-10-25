<script lang="ts">
	// Per-service Jira config editor for the /errors "New Jira issue" link - Admin-only (gated by the
	// caller), backed by IssueTrackerEndpoints (ADR-0274). Same icon-triggered mini-form popover as
	// SourceLinkPopover.
	import * as Popover from '#lib/components/ui/popover/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import TicketIcon from '@lucide/svelte/icons/ticket';
	import type { IssueTrackerConfig } from '#lib/errors/issue-links.ts';
	import * as m from '#lib/paraglide/messages.js';

	let {
		serviceName,
		config,
		onSave,
		onRemove
	}: {
		serviceName: string;
		config: IssueTrackerConfig | undefined;
		onSave: (config: IssueTrackerConfig) => Promise<void>;
		onRemove: () => Promise<void>;
	} = $props();

	let open = $state(false);
	let siteUrl = $state('');
	let projectId = $state('');
	let issueTypeId = $state('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	// Re-seed from the stored config each time this opens, so a stale draft never shows.
	$effect(() => {
		if (open) {
			siteUrl = config?.siteUrl ?? '';
			projectId = config?.projectId ?? '';
			issueTypeId = config?.issueTypeId ?? '';
			error = null;
		}
	});

	async function run(action: () => Promise<void>): Promise<void> {
		saving = true;
		error = null;
		try {
			await action();
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}

	function save(): Promise<void> {
		if (!/^https?:\/\/\S+$/i.test(siteUrl.trim())) {
			error = m.issueTrackerPopover_invalidUrl();
			return Promise.resolve();
		}
		if (!/^\d+$/.test(projectId.trim()) || (issueTypeId.trim() && !/^\d+$/.test(issueTypeId.trim()))) {
			error = m.issueTrackerPopover_invalidIds();
			return Promise.resolve();
		}
		return run(() => onSave({ serviceName, provider: 'Jira', siteUrl: siteUrl.trim(), projectId: projectId.trim(), issueTypeId: issueTypeId.trim() }));
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={config ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={m.issueTrackerPopover_title()}
			>
				<TicketIcon class="size-3.5" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-80" align="end">
		<p class="mb-1 text-sm font-medium">{m.issueTrackerPopover_titleForService({ serviceName })}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.issueTrackerPopover_description()}</p>
		<div class="space-y-2">
			<Input bind:value={siteUrl} placeholder={m.issueTrackerPopover_siteUrlPlaceholder()} class="h-8" disabled={saving} />
			<Input bind:value={projectId} placeholder={m.issueTrackerPopover_projectIdPlaceholder()} inputmode="numeric" class="h-8" disabled={saving} />
			<Input bind:value={issueTypeId} placeholder={m.issueTrackerPopover_issueTypeIdPlaceholder()} inputmode="numeric" class="h-8" disabled={saving} />
		</div>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={() => run(onRemove)} disabled={saving || !config}>
				{m.issueTrackerPopover_remove()}
			</Button>
			<Button size="sm" onclick={save} disabled={saving}>
				{m.issueTrackerPopover_save()}
			</Button>
		</div>
	</Popover.Content>
</Popover.Root>
