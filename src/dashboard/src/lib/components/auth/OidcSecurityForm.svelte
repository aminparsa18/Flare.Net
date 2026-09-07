<script lang="ts">
	// One section of the consolidated /auth page - lets each self-hosted Flare operator
	// point Flare at any standards-compliant OpenID Connect provider (Okta, Auth0,
	// Keycloak, Authentik, ...), not just Microsoft Entra ID, mirroring Seq's own generic
	// "OpenID Connect" Security screen (Authority/Client id/Client secret/Scopes, plus a
	// computed Callback URL). Closely modeled on EntraSecurityForm.svelte - like Entra,
	// settings only take effect after a Flare.Api restart (same OpenIdConnectOptions
	// per-process caching reason - see OidcOpenIdConnectOptionsConfigurator's remarks) -
	// deliberately not attempting to detect/poll for that, just saying so.
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Switch } from '$lib/components/ui/switch';
	import * as Select from '$lib/components/ui/select';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import { oidcSettingsContext } from '$lib/oidc-settings/context';
	import type { UserRole } from '$lib/auth-api';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import * as m from '$lib/paraglide/messages';

	const oidc = oidcSettingsContext.get();

	const ROLES: UserRole[] = ['Admin', 'Member', 'Viewer'];

	function roleLabel(role: UserRole): string {
		switch (role) {
			case 'Admin':
				return m.userRole_admin();
			case 'Member':
				return m.userRole_member();
			case 'Viewer':
				return m.userRole_viewer();
		}
	}

	let enabled = $state(false);
	let displayName = $state('');
	let authority = $state('');
	let clientId = $state('');
	let clientSecret = $state('');
	let scopes = $state('');
	let roleClaimName = $state('');
	let defaultRole = $state<UserRole>('Viewer');

	// Re-seeds the form whenever oidc.settings changes identity - on initial load, and
	// again after a successful save (deliberately, so the form reflects exactly what was
	// just persisted) - same "seed once per data change, not per render" shape
	// EntraSecurityForm.svelte's own effect already establishes.
	$effect(() => {
		if (oidc.settings) {
			enabled = oidc.settings.enabled;
			displayName = oidc.settings.displayName ?? '';
			authority = oidc.settings.authority ?? '';
			clientId = oidc.settings.clientId ?? '';
			clientSecret = '';
			scopes = oidc.settings.scopes;
			roleClaimName = oidc.settings.roleClaimName;
			defaultRole = oidc.settings.defaultRole;
		}
	});

	let copied = $state(false);
	async function copyRedirectUri(): Promise<void> {
		if (!oidc.settings) return;
		await navigator.clipboard.writeText(oidc.settings.redirectUri);
		copied = true;
		setTimeout(() => (copied = false), 1500);
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await oidc.save({
			enabled,
			displayName: displayName.trim() || null,
			authority: authority.trim() || null,
			clientId: clientId.trim() || null,
			// Blank means "leave whatever's already stored unchanged" - only send a real
			// value when the Admin actually typed a new secret.
			clientSecret: clientSecret.trim() || null,
			scopes: scopes.trim(),
			roleClaimName: roleClaimName.trim(),
			defaultRole
		});
	}
</script>

{#if oidc.loading}
	<div class="flex items-center justify-center py-12">
		<Spinner />
	</div>
{:else if oidc.error}
	<p class="text-destructive text-sm">{oidc.error}</p>
{:else if oidc.settings}
	<Card.Root class="shrink-0">
		<Card.Header>
			<Card.Title>{m.oidcSecurityForm_title()}</Card.Title>
			<Card.Description>{m.oidcSecurityForm_description()}</Card.Description>
		</Card.Header>
		<Card.Content>
			<form class="flex flex-col gap-4" onsubmit={handleSubmit}>
				{#if oidc.saveError}
					<Alert variant="destructive">
						<AlertDescription>{oidc.saveError}</AlertDescription>
					</Alert>
				{/if}
				{#if oidc.justSaved}
					<Alert>
						<AlertDescription>{m.oidcSecurityForm_savedRestart()}</AlertDescription>
					</Alert>
				{/if}

				<div class="flex flex-col gap-1">
					<label for="oidc-redirect-uri" class="text-xs font-medium">{m.oidcSecurityForm_callbackUrlLabel()}</label>
					<div class="flex gap-1">
						<Input id="oidc-redirect-uri" value={oidc.settings.redirectUri} readonly class="font-mono text-xs" />
						<Button
							type="button"
							variant="outline"
							size="icon"
							onclick={copyRedirectUri}
							title={m.oidcSecurityForm_copyTitle()}
						>
							{#if copied}
								<CheckIcon />
							{:else}
								<CopyIcon />
							{/if}
						</Button>
					</div>
					<p class="text-muted-foreground text-xs">{@html m.oidcSecurityForm_callbackUrlHint({ exact: '<strong>exact</strong>' })}</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="oidc-display-name" class="text-xs font-medium">{m.oidcSecurityForm_displayNameLabel()}</label>
					<Input id="oidc-display-name" bind:value={displayName} placeholder="Okta" />
					<p class="text-muted-foreground text-xs">{m.oidcSecurityForm_displayNameHint({ name: displayName || '…' })}</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="oidc-authority" class="text-xs font-medium">{m.oidcSecurityForm_authorityLabel()}</label>
					<Input id="oidc-authority" bind:value={authority} placeholder="https://example.okta.com" />
					<p class="text-muted-foreground text-xs">{m.oidcSecurityForm_authorityHint()}</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="oidc-client-id" class="text-xs font-medium">{m.oidcSecurityForm_clientIdLabel()}</label>
					<Input id="oidc-client-id" bind:value={clientId} />
				</div>

				<div class="flex flex-col gap-1">
					<label for="oidc-client-secret" class="text-xs font-medium">{m.oidcSecurityForm_clientSecretLabel()}</label>
					<Input
						id="oidc-client-secret"
						type="password"
						bind:value={clientSecret}
						placeholder={oidc.settings.hasClientSecret
							? m.entraSecurityForm_clientSecretPlaceholderUnchanged()
							: m.entraSecurityForm_clientSecretPlaceholderNew()}
					/>
					<p class="text-muted-foreground text-xs">{m.entraSecurityForm_clientSecretHint()}</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="oidc-scopes" class="text-xs font-medium">{m.oidcSecurityForm_scopesLabel()}</label>
					<Input id="oidc-scopes" bind:value={scopes} class="font-mono text-xs" />
					<p class="text-muted-foreground text-xs">{m.oidcSecurityForm_scopesHint()}</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="oidc-role-claim" class="text-xs font-medium">{m.oidcSecurityForm_roleClaimLabel()}</label>
					<Input id="oidc-role-claim" bind:value={roleClaimName} class="font-mono text-xs" />
					<p class="text-muted-foreground text-xs">
						{@html m.oidcSecurityForm_roleClaimHint({
							roleCodes: '<code>Admin</code>/<code>Member</code>/<code>Viewer</code>',
							rolesClaim: '<code>roles</code>'
						})}
					</p>
				</div>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.oidcSecurityForm_defaultRoleLabel()}</span>
					<Select.Root type="single" value={defaultRole} onValueChange={(v) => v && (defaultRole = v as UserRole)}>
						<Select.Trigger class="w-28">
							{roleLabel(defaultRole)}
						</Select.Trigger>
						<Select.Content>
							{#each ROLES as role (role)}
								<Select.Item value={role} label={roleLabel(role)} />
							{/each}
						</Select.Content>
					</Select.Root>
					<p class="text-muted-foreground text-xs">{m.oidcSecurityForm_defaultRoleHint()}</p>
				</div>

				<div class="flex items-center gap-2">
					<Switch bind:checked={enabled} />
					<span class="text-xs">{m.oidcSecurityForm_enabled()}</span>
				</div>

				<Button type="submit" disabled={oidc.saving} class="self-start">
					{oidc.saving ? m.oidcSecurityForm_saving() : m.oidcSecurityForm_save()}
				</Button>
			</form>
		</Card.Content>
	</Card.Root>
{/if}
