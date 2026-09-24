<script lang="ts">
	// Same two-step create -> reveal-once dialog as CreateAccessTokenDialog.svelte, minus
	// the expiry field (ingest keys don't expire - they're revoked). The terminal's
	// `apikey create` command is the other front end for the same POST.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { ingestKeysContext } from '$lib/ingest-keys/context';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import * as m from '$lib/paraglide/messages';

	const keys = ingestKeysContext.get();

	let name = $state('');
	let copied = $state(false);

	$effect(() => {
		if (keys.createOpen) {
			name = '';
			copied = false;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (keys.saving) return;
		if (!next) keys.closeCreate();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await keys.create(name);
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
