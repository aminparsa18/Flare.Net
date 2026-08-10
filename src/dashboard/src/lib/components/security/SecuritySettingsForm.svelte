<script lang="ts">
	// Admin-only Security screen - lets each self-hosted Flare operator paste in their
	// own Entra ID App Registration values instead of editing docker-compose/.env,
	// mirroring Seq's own Security settings page (the reference this feature was scoped
	// from - see docs/auth.md's "Microsoft Entra ID (SSO)" section and Planning.md's v12
	// addendum). Settings only take effect after a Flare.Api restart - deliberately not
	// attempting to detect/poll for that, just saying so.
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Switch } from '$lib/components/ui/switch';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import { entraSettingsContext } from '$lib/entra-settings/context';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';

	const entra = entraSettingsContext.get();

	let enabled = $state(false);
	let tenantId = $state('');
	let clientId = $state('');
	let clientSecret = $state('');

	// Re-seeds the form whenever entra.settings changes identity - on initial load, and
	// again after a successful save (deliberately, so the form reflects exactly what was
	// just persisted) - never on a mere keystroke, since typing here doesn't touch
	// entra.settings itself. Same "seed once per data change, not per render" shape
	// AlertRuleFormDialog.svelte's formTarget effect already establishes.
	$effect(() => {
		if (entra.settings) {
			enabled = entra.settings.enabled;
			tenantId = entra.settings.tenantId ?? '';
			clientId = entra.settings.clientId ?? '';
			clientSecret = '';
		}
	});

	let copied = $state(false);
	async function copyRedirectUri(): Promise<void> {
		if (!entra.settings) return;
		await navigator.clipboard.writeText(entra.settings.redirectUri);
		copied = true;
		setTimeout(() => (copied = false), 1500);
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await entra.save({
			enabled,
			tenantId: tenantId.trim() || null,
			clientId: clientId.trim() || null,
			// Blank means "leave whatever's already stored unchanged" - only send a real
			// value when the Admin actually typed a new secret.
			clientSecret: clientSecret.trim() || null
		});
	}
</script>

<div class="border-b px-4 py-3">
	<h1 class="text-sm font-semibold">Security</h1>
	<p class="text-muted-foreground text-xs">Configure Microsoft Entra ID (SSO) for this Flare instance.</p>
</div>

<div class="min-h-0 flex-1 overflow-y-auto p-4">
	{#if entra.loading}
		<div class="flex items-center justify-center py-12">
			<Spinner />
		</div>
	{:else if entra.error}
		<p class="text-destructive text-sm">{entra.error}</p>
	{:else if entra.settings}
		<Card.Root class="max-w-xl">
			<Card.Header>
				<Card.Title>Microsoft Entra ID</Card.Title>
				<Card.Description>
					Before Flare can authenticate users against your Entra ID directory, it must be registered as an
					<em>Application</em> in that directory. Create an App Registration in the Azure portal and paste the resulting
					values below - see docs/auth.md for the full walkthrough. Each self-hosted Flare instance uses its own App
					Registration; nothing here is shared across deployments.
				</Card.Description>
			</Card.Header>
			<Card.Content>
				<form class="flex flex-col gap-4" onsubmit={handleSubmit}>
					{#if entra.saveError}
						<Alert variant="destructive">
							<AlertDescription>{entra.saveError}</AlertDescription>
						</Alert>
					{/if}
					{#if entra.justSaved}
						<Alert>
							<AlertDescription>Saved. Restart Flare.Api for this to take effect.</AlertDescription>
						</Alert>
					{/if}

					<div class="flex flex-col gap-1">
						<label for="redirect-uri" class="text-xs font-medium">Redirect URI</label>
						<div class="flex gap-1">
							<Input id="redirect-uri" value={entra.settings.redirectUri} readonly class="font-mono text-xs" />
							<Button type="button" variant="outline" size="icon" onclick={copyRedirectUri} title="Copy">
								{#if copied}
									<CheckIcon />
								{:else}
									<CopyIcon />
								{/if}
							</Button>
						</div>
						<p class="text-muted-foreground text-xs">
							You must configure this <strong>exact</strong> redirect URI (reply URL) in your Entra ID app registration.
						</p>
					</div>

					<div class="flex flex-col gap-1">
						<label for="tenant-id" class="text-xs font-medium">Directory (tenant) ID</label>
						<Input id="tenant-id" bind:value={tenantId} placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
					</div>

					<div class="flex flex-col gap-1">
						<label for="client-id" class="text-xs font-medium">Application (client) ID</label>
						<Input id="client-id" bind:value={clientId} placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
					</div>

					<div class="flex flex-col gap-1">
						<label for="client-secret" class="text-xs font-medium">Client secret</label>
						<Input
							id="client-secret"
							type="password"
							bind:value={clientSecret}
							placeholder={entra.settings.hasClientSecret ? '•••••••••••••••••••••• (unchanged)' : 'Paste the client secret value'}
						/>
						<p class="text-muted-foreground text-xs">
							Client secret assigned to the Flare application. Leave blank to keep the currently-saved value - it is never
							displayed once set.
						</p>
					</div>

					<div class="flex items-center gap-2">
						<Switch bind:checked={enabled} />
						<span class="text-xs">Enabled</span>
					</div>

					<Button type="submit" disabled={entra.saving} class="self-start">
						{entra.saving ? 'Saving…' : 'Save'}
					</Button>
				</form>
			</Card.Content>
		</Card.Root>
	{/if}
</div>
