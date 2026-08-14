<script lang="ts">
	// The consolidated /auth page's umbrella switch - opt-in auth (docs/auth.md): a
	// fresh Flare instance has no login requirement at all until this is turned on.
	// Reads Entra/LDAP's own enabled state (already loaded by EntraSecurityForm.svelte/
	// LdapSecurityForm.svelte on the same page) purely for the client-side "would this
	// lock everyone out" guard below - the server enforces the same rule regardless
	// (AuthSettingsEndpoints.HandlePutAsync), this is just so the Save button doesn't
	// invite a 400 the UI could have prevented.
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Switch } from '$lib/components/ui/switch';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import { authSettingsContext } from '$lib/auth-settings/context';
	import { entraSettingsContext } from '$lib/entra-settings/context';
	import { ldapSettingsContext } from '$lib/ldap-settings/context';
	import { authContext } from '$lib/auth/context';

	const authSettings = authSettingsContext.get();
	const entraSettings = entraSettingsContext.get();
	const ldapSettings = ldapSettingsContext.get();
	const auth = authContext.get();

	let enabled = $state(false);
	let localEnabled = $state(true);

	// Re-seeds whenever authSettings.settings changes identity - on initial load and
	// again after a successful save, same "seed once per data change" shape
	// SecuritySettingsForm.svelte's own effect already established.
	$effect(() => {
		if (authSettings.settings) {
			enabled = authSettings.settings.enabled;
			localEnabled = authSettings.settings.localEnabled;
		}
	});

	const wouldLockOut = $derived(
		enabled && !localEnabled && entraSettings.settings?.enabled !== true && ldapSettings.settings?.enabled !== true
	);

	async function handleSave(): Promise<void> {
		const saved = await authSettings.save({ enabled, localEnabled });
		if (saved) {
			// Propagate into the shared AuthState so +layout.svelte's route guard
			// re-evaluates on its next effect run - without this, a visitor who just
			// turned auth on (while browsing anonymously, since auth was off) would stay
			// on /auth until some unrelated navigation happened to re-trigger the guard.
			auth.authEnabled = saved.enabled;
		}
	}
</script>

<Card.Root class="max-w-xl">
	<Card.Header>
		<Card.Title>Authentication</Card.Title>
		<Card.Description>
			Off by default - anyone who can reach this dashboard has full access. Turn this on to require signing in, then
			enable and configure at least one method below.
		</Card.Description>
	</Card.Header>
	<Card.Content>
		{#if authSettings.loading}
			<div class="flex justify-center py-4"><Spinner /></div>
		{:else if authSettings.settings}
			<div class="flex flex-col gap-4">
				{#if authSettings.saveError}
					<Alert variant="destructive">
						<AlertDescription>{authSettings.saveError}</AlertDescription>
					</Alert>
				{/if}
				{#if wouldLockOut}
					<Alert variant="destructive">
						<AlertDescription>
							Enabling this with local sign-in off and no other method enabled would lock everyone out. Enable local
							sign-in or configure another method (below) first.
						</AlertDescription>
					</Alert>
				{/if}

				<div class="flex items-center gap-2">
					<Switch bind:checked={enabled} />
					<span class="text-xs font-medium">Require sign-in</span>
				</div>
				<div class="flex items-center gap-2">
					<Switch bind:checked={localEnabled} />
					<span class="text-xs">Local username/password</span>
				</div>

				<Button onclick={handleSave} disabled={authSettings.saving || wouldLockOut} class="self-start">
					{authSettings.saving ? 'Saving…' : 'Save'}
				</Button>
			</div>
		{/if}
	</Card.Content>
</Card.Root>
