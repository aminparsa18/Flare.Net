<script lang="ts">
	// Per-service source repo editor for the /errors stack-trace links - Admin-only (gated by the
	// caller), backed by SourceLinkEndpoints (ADR-0095). Same icon-triggered mini-form popover as
	// ApdexThresholdPopover.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import LinkIcon from '@lucide/svelte/icons/link-2';
	import type { SourceLinkConfig, SourceLinkProvider } from '$lib/errors/source-links';
	import * as m from '$lib/paraglide/messages';

	let {
		serviceName,
		config,
		onSave,
		onRemove
	}: {
		serviceName: string;
		config: SourceLinkConfig | undefined;
		onSave: (config: SourceLinkConfig) => Promise<void>;
		onRemove: () => Promise<void>;
	} = $props();

	let open = $state(false);
	let provider = $state<SourceLinkProvider>('GitHub');
	let repoUrl = $state('');
	let defaultRef = $state('');
	let pathPrefix = $state('');
	// Write-only: always starts blank; blank on save keeps the stored token.
	let accessToken = $state('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	// Re-seed from the stored config each time this opens, so a stale draft never shows.
	$effect(() => {
		if (open) {
			provider = config?.provider ?? 'GitHub';
			repoUrl = config?.repoUrl ?? '';
			defaultRef = config?.defaultRef ?? '';
			pathPrefix = config?.pathPrefix ?? '';
			accessToken = '';
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
		if (!/^https?:\/\/\S+$/i.test(repoUrl.trim())) {
			error = m.sourceLinkPopover_invalidUrl();
			return Promise.resolve();
		}
		return run(() => onSave({ serviceName, provider, repoUrl: repoUrl.trim(), defaultRef: defaultRef.trim(), pathPrefix: pathPrefix.trim(), accessToken: accessToken.trim() || undefined }));
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
				title={m.sourceLinkPopover_title()}
			>
				<LinkIcon class="size-3.5" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-80" align="end">
		<p class="mb-1 text-sm font-medium">{m.sourceLinkPopover_titleForService({ serviceName })}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.sourceLinkPopover_description()}</p>
		<div class="space-y-2">
			<select bind:value={provider} disabled={saving} class="border-input bg-background h-8 w-full rounded-md border px-2 text-sm">
				<option value="GitHub">GitHub</option>
				<option value="GitLab">GitLab</option>
				<option value="AzureDevOps">Azure DevOps</option>
			</select>
			<Input bind:value={repoUrl} placeholder={m.sourceLinkPopover_repoUrlPlaceholder()} class="h-8" disabled={saving} />
			<Input bind:value={defaultRef} placeholder={m.sourceLinkPopover_defaultRefPlaceholder()} class="h-8" disabled={saving} />
			<Input bind:value={pathPrefix} placeholder={m.sourceLinkPopover_pathPrefixPlaceholder()} class="h-8" disabled={saving} />
			<Input
				type="password"
				autocomplete="off"
				bind:value={accessToken}
				placeholder={config?.hasAccessToken ? m.sourceLinkPopover_tokenSavedPlaceholder() : m.sourceLinkPopover_tokenPlaceholder()}
				class="h-8"
				disabled={saving}
			/>
		</div>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={() => run(onRemove)} disabled={saving || !config}>
				{m.sourceLinkPopover_remove()}
			</Button>
			<Button size="sm" onclick={save} disabled={saving}>
				{m.sourceLinkPopover_save()}
			</Button>
		</div>
	</Popover.Content>
</Popover.Root>
