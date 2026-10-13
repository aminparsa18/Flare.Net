<script lang="ts">
	// Same two-step create -> reveal-once dialog as CreateAccessTokenDialog.svelte, with the
	// same optional expiry (ADR-0207). The terminal's
	// `apikey create` command is the other front end for the same POST.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { Alert, AlertDescription } from '#lib/components/ui/alert/index.ts';
	import ProjectPicker from '#lib/components/projects/ProjectPicker.svelte';
	import { projects } from '#lib/projects/store.svelte.ts';
	import { ingestKeysContext } from '#lib/ingest-keys/context.ts';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import * as m from '#lib/paraglide/messages.js';

	const keys = ingestKeysContext.get();

	let name = $state('');
	let projectId = $state<string | null>(null);
	let neverExpires = $state(true);
	let expiresInDays = $state(90);
	let copied = $state(false);

	$effect(() => {
		if (keys.createOpen) {
			name = '';
			projectId = projects.defaultForNew;
			neverExpires = true;
			expiresInDays = 90;
			copied = false;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (keys.saving) return;
		if (!next) keys.closeCreate();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await keys.create(name, projectId, neverExpires ? null : expiresInDays);
	}

	async function handleCopy(): Promise<void> {
		if (!keys.revealedKey) return;
		await navigator.clipboard.writeText(keys.revealedKey);
		copied = true;
		setTimeout(() => (copied = false), 1500);
	}
</script>

<Dialog.Root open={keys.createOpen} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		{#if keys.revealedKey}
			<Dialog.Header>
				<Dialog.Title>{m.createIngestKeyDialog_revealTitle()}</Dialog.Title>
				<Dialog.Description>{m.createIngestKeyDialog_revealDescription()}</Dialog.Description>
			</Dialog.Header>
			<div class="flex items-center gap-2">
				<code class="bg-muted flex-1 overflow-x-auto rounded-md border px-3 py-2 text-xs break-all">{keys.revealedKey}</code>
				<Button type="button" variant="outline" size="icon" onclick={handleCopy} title={m.createIngestKeyDialog_copy()}>
					{#if copied}<CheckIcon />{:else}<CopyIcon />{/if}
				</Button>
			</div>
			<Alert>
				<TriangleAlertIcon />
				<AlertDescription>{m.createIngestKeyDialog_revealWarning()}</AlertDescription>
			</Alert>
			<Dialog.Footer>
				<Button type="button" onclick={() => keys.closeCreate()}>{m.createIngestKeyDialog_done()}</Button>
			</Dialog.Footer>
		{:else}
			<Dialog.Header>
				<Dialog.Title>{m.createIngestKeyDialog_title()}</Dialog.Title>
				<Dialog.Description>{m.createIngestKeyDialog_description()}</Dialog.Description>
			</Dialog.Header>
			<form class="space-y-4" onsubmit={handleSubmit}>
				<div class="space-y-2">
					<label for="ingest-key-name" class="text-sm font-medium">{m.createIngestKeyDialog_nameLabel()}</label>
					<Input id="ingest-key-name" bind:value={name} required placeholder={m.createIngestKeyDialog_namePlaceholder()} />
				</div>
				<ProjectPicker bind:value={projectId} includeAll />
				<div class="space-y-2">
					<span class="text-sm font-medium">{m.createAccessTokenDialog_expiryLabel()}</span>
					<div class="flex items-center gap-4 text-sm">
						<label class="flex items-center gap-2">
							<input type="radio" name="ingest-expiry-mode" checked={neverExpires} onchange={() => (neverExpires = true)} />
							{m.createAccessTokenDialog_expiryNever()}
						</label>
						<label class="flex items-center gap-2">
							<input type="radio" name="ingest-expiry-mode" checked={!neverExpires} onchange={() => (neverExpires = false)} />
							{m.createAccessTokenDialog_expiryCustomLabel()}
						</label>
						{#if !neverExpires}
							<Input type="number" min="1" max="3650" bind:value={expiresInDays} class="w-20" />
						{/if}
					</div>
				</div>
				{#if keys.saveError}
					<p class="text-destructive text-sm">{keys.saveError}</p>
				{/if}
				<Dialog.Footer>
					<Button type="button" variant="outline" onclick={() => keys.closeCreate()}>{m.createIngestKeyDialog_cancel()}</Button>
					<Button type="submit" disabled={keys.saving}>
						{#if keys.saving}<Spinner class="size-4" />{/if}
						{m.createIngestKeyDialog_create()}
					</Button>
				</Dialog.Footer>
			</form>
		{/if}
	</Dialog.Content>
</Dialog.Root>
