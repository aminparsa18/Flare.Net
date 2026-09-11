<script lang="ts">
	// Two-step dialog: a plain create form (name + optional expiry), then - on success -
	// swaps in place to a "reveal" step showing the raw token exactly once. Mirrors
	// SaveViewDialog.svelte's form shape for the first step; the second step has no
	// existing precedent in this dashboard (ingest API keys' equivalent reveal-once flow
	// only exists in the terminal modal, as plain text - see terminal/commands/apikey.ts),
	// so it's original to this component.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { accessTokensContext } from '$lib/access-tokens/context';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import * as m from '$lib/paraglide/messages';

	const tokens = accessTokensContext.get();

	let name = $state('');
	let neverExpires = $state(true);
	let expiresInDays = $state(90);
	let copied = $state(false);

	$effect(() => {
		if (tokens.createOpen) {
			name = '';
			neverExpires = true;
			expiresInDays = 90;
			copied = false;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (tokens.saving) return;
		if (!next) tokens.closeCreate();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await tokens.create(name, neverExpires ? null : expiresInDays);
	}

	async function handleCopy(): Promise<void> {
		if (!tokens.revealedToken) return;
		await navigator.clipboard.writeText(tokens.revealedToken);
		copied = true;
		setTimeout(() => (copied = false), 1500);
	}
</script>

<Dialog.Root open={tokens.createOpen} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		{#if tokens.revealedToken}
			<Dialog.Header>
				<Dialog.Title>{m.createAccessTokenDialog_revealTitle()}</Dialog.Title>
				<Dialog.Description>{m.createAccessTokenDialog_revealDescription()}</Dialog.Description>
			</Dialog.Header>
			<div class="flex items-center gap-2">
				<code class="bg-muted flex-1 overflow-x-auto rounded-md border px-3 py-2 text-xs break-all">{tokens.revealedToken}</code>
				<Button type="button" variant="outline" size="icon" onclick={handleCopy} title={m.createAccessTokenDialog_copy()}>
					{#if copied}<CheckIcon />{:else}<CopyIcon />{/if}
				</Button>
			</div>
			<Alert>
				<TriangleAlertIcon />
				<AlertDescription>{m.createAccessTokenDialog_revealWarning()}</AlertDescription>
			</Alert>
			<Dialog.Footer>
				<Button type="button" onclick={() => tokens.closeCreate()}>{m.createAccessTokenDialog_done()}</Button>
			</Dialog.Footer>
		{:else}
			<Dialog.Header>
				<Dialog.Title>{m.createAccessTokenDialog_title()}</Dialog.Title>
				<Dialog.Description>{m.createAccessTokenDialog_description()}</Dialog.Description>
			</Dialog.Header>
			<form class="space-y-4" onsubmit={handleSubmit}>
				<div class="space-y-2">
					<label for="access-token-name" class="text-sm font-medium">{m.createAccessTokenDialog_nameLabel()}</label>
					<Input id="access-token-name" bind:value={name} required placeholder={m.createAccessTokenDialog_namePlaceholder()} />
				</div>
				<div class="space-y-2">
					<span class="text-sm font-medium">{m.createAccessTokenDialog_expiryLabel()}</span>
					<div class="flex items-center gap-2">
						<label class="flex items-center gap-2 text-sm">
							<input type="radio" name="expiry-mode" checked={neverExpires} onchange={() => (neverExpires = true)} />
							{m.createAccessTokenDialog_expiryNever()}
						</label>
						<label class="flex items-center gap-2 text-sm">
							<input type="radio" name="expiry-mode" checked={!neverExpires} onchange={() => (neverExpires = false)} />
							{m.createAccessTokenDialog_expiryCustomLabel()}
						</label>
						{#if !neverExpires}
							<Input type="number" min="1" max="365" bind:value={expiresInDays} class="w-20" />
						{/if}
					</div>
				</div>
				{#if tokens.saveError}
					<p class="text-destructive text-sm">{tokens.saveError}</p>
				{/if}
				<Dialog.Footer>
					<Button type="button" variant="outline" onclick={() => tokens.closeCreate()}>{m.createAccessTokenDialog_cancel()}</Button>
					<Button type="submit" disabled={tokens.saving}>
						{#if tokens.saving}<Spinner class="size-4" />{/if}
						{m.createAccessTokenDialog_create()}
					</Button>
				</Dialog.Footer>
			</form>
		{/if}
	</Dialog.Content>
</Dialog.Root>
