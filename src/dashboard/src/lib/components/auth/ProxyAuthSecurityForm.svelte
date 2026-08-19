<script lang="ts">
	// One section of the consolidated /auth page - lets each self-hosted Flare operator
	// trust an identity header an already-authenticating reverse proxy (Authelia,
	// Authentik, oauth2-proxy, Cloudflare Access, Tailscale Serve, ...) injects, instead
	// of Flare talking to an IdP itself. Mirrors LdapSecurityForm.svelte's shape most
	// closely: no secret field (nothing to mask here), no restart-required banner -
	// settings take effect on the very next login attempt, same as LDAP's (see
	// ProxyAuthSettingsEndpoints' remarks).
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Switch } from '$lib/components/ui/switch';
	import * as Select from '$lib/components/ui/select';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import { proxyAuthSettingsContext } from '$lib/proxy-auth-settings/context';
	import type { UserRole } from '$lib/auth-api';

	const proxyAuth = proxyAuthSettingsContext.get();

	const ROLES: UserRole[] = ['Admin', 'Member', 'Viewer'];

	let enabled = $state(false);
	let headerName = $state('');
	let trustedProxyCidrs = $state('');
	let groupsHeaderName = $state('');
	let adminGroup = $state('');
	let memberGroup = $state('');
	let viewerGroup = $state('');
	let defaultRole = $state<UserRole>('Viewer');
	let logoutRedirectUrl = $state('');

	// Re-seeds the form whenever proxyAuth.settings changes identity - on initial load,
	// and again after a successful save - same "seed once per data change" shape
	// EntraSecurityForm.svelte's own effect already establishes.
	$effect(() => {
		if (proxyAuth.settings) {
			enabled = proxyAuth.settings.enabled;
			headerName = proxyAuth.settings.headerName;
			trustedProxyCidrs = proxyAuth.settings.trustedProxyCidrs;
			groupsHeaderName = proxyAuth.settings.groupsHeaderName ?? '';
			adminGroup = proxyAuth.settings.adminGroup ?? '';
			memberGroup = proxyAuth.settings.memberGroup ?? '';
			viewerGroup = proxyAuth.settings.viewerGroup ?? '';
			defaultRole = proxyAuth.settings.defaultRole;
			logoutRedirectUrl = proxyAuth.settings.logoutRedirectUrl ?? '';
		}
	});

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await proxyAuth.save({
			enabled,
			headerName: headerName.trim(),
			trustedProxyCidrs: trustedProxyCidrs.trim(),
			groupsHeaderName: groupsHeaderName.trim() || null,
			adminGroup: adminGroup.trim() || null,
			memberGroup: memberGroup.trim() || null,
			viewerGroup: viewerGroup.trim() || null,
			defaultRole,
			logoutRedirectUrl: logoutRedirectUrl.trim() || null
		});
	}
</script>

{#if proxyAuth.loading}
	<div class="flex items-center justify-center py-12">
		<Spinner />
	</div>
{:else if proxyAuth.error}
	<p class="text-destructive text-sm">{proxyAuth.error}</p>
{:else if proxyAuth.settings}
	<Card.Root class="shrink-0">
		<Card.Header>
			<Card.Title>Reverse proxy</Card.Title>
			<Card.Description>
				Trust an identity header from a reverse proxy that already authenticates requests in front of Flare
				(Authelia, Authentik, oauth2-proxy, Cloudflare Access, Tailscale Serve, ...) - no separate sign-in flow,
				Flare just reads the header. See docs/auth.md for the full walkthrough and the security model.
			</Card.Description>
		</Card.Header>
		<Card.Content>
			<form class="flex flex-col gap-4" onsubmit={handleSubmit}>
				{#if proxyAuth.saveError}
					<Alert variant="destructive">
						<AlertDescription>{proxyAuth.saveError}</AlertDescription>
					</Alert>
				{/if}
				{#if proxyAuth.justSaved}
					<Alert>
						<AlertDescription>Saved.</AlertDescription>
					</Alert>
				{/if}

				<div class="flex flex-col gap-1">
					<label for="proxy-header-name" class="text-xs font-medium">Header name</label>
					<Input id="proxy-header-name" bind:value={headerName} placeholder="Remote-User" class="font-mono text-xs" />
					<p class="text-muted-foreground text-xs">The request header the proxy sets to the signed-in user's identity.</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="proxy-trusted-cidrs" class="text-xs font-medium">Trusted proxy CIDRs</label>
					<Textarea
						id="proxy-trusted-cidrs"
						bind:value={trustedProxyCidrs}
						placeholder={'172.18.0.0/16'}
						rows={3}
						class="font-mono text-xs"
					/>
					<p class="text-muted-foreground text-xs">
						One or more CIDR ranges, one per line - <strong>required</strong> to enable this method. The header above
						is only trusted from a caller whose own address falls inside one of these ranges, since the header itself
						can be spoofed by anyone who can reach Flare.Api directly.
					</p>
				</div>

				<details class="text-xs">
					<summary class="text-muted-foreground cursor-pointer font-medium">Advanced: role mapping</summary>
					<div class="mt-3 flex flex-col gap-3">
						<div class="flex flex-col gap-1">
							<label for="proxy-groups-header" class="text-xs font-medium">Groups header name</label>
							<Input
								id="proxy-groups-header"
								bind:value={groupsHeaderName}
								placeholder="Optional, e.g. X-Forwarded-Groups"
								class="font-mono text-xs"
							/>
							<p class="text-muted-foreground text-xs">
								A second header carrying comma-separated group names. Leave blank to always assign Default role below.
							</p>
						</div>
						<div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
							<div class="flex flex-col gap-1">
								<label for="proxy-admin-group" class="text-xs font-medium">Admin group</label>
								<Input id="proxy-admin-group" bind:value={adminGroup} placeholder="Optional" />
							</div>
							<div class="flex flex-col gap-1">
								<label for="proxy-member-group" class="text-xs font-medium">Member group</label>
								<Input id="proxy-member-group" bind:value={memberGroup} placeholder="Optional" />
							</div>
							<div class="flex flex-col gap-1">
								<label for="proxy-viewer-group" class="text-xs font-medium">Viewer group</label>
								<Input id="proxy-viewer-group" bind:value={viewerGroup} placeholder="Optional" />
							</div>
						</div>
					</div>
				</details>

				<details class="text-xs">
					<summary class="text-muted-foreground cursor-pointer font-medium">Advanced: logout</summary>
					<div class="mt-3 flex flex-col gap-1">
						<label for="proxy-logout-redirect-url" class="text-xs font-medium">Logout redirect URL</label>
						<Input
							id="proxy-logout-redirect-url"
							bind:value={logoutRedirectUrl}
							placeholder="Optional, e.g. https://proxy.example.com/oauth2/sign_out"
							class="font-mono text-xs"
						/>
						<p class="text-muted-foreground text-xs">
							Flare can't log a user out of the proxy's own session - it only clears Flare's own cookie. Left blank,
							clicking "Log out" just returns to /login, which signs back in silently as long as the proxy keeps
							sending the header. Set this to your proxy's own sign-out URL (or the identity provider's) to send the
							browser there instead.
						</p>
					</div>
				</details>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">Default role</span>
					<Select.Root type="single" value={defaultRole} onValueChange={(v) => v && (defaultRole = v as UserRole)}>
						<Select.Trigger class="w-28">
							{defaultRole}
						</Select.Trigger>
						<Select.Content>
							{#each ROLES as role (role)}
								<Select.Item value={role} label={role} />
							{/each}
						</Select.Content>
					</Select.Root>
					<p class="text-muted-foreground text-xs">
						Assigned on first sign-in when no groups header is configured, or its value matches none of the three
						groups above.
					</p>
				</div>

				<div class="flex items-center gap-2">
					<Switch bind:checked={enabled} />
					<span class="text-xs">Enabled</span>
				</div>

				<Button type="submit" disabled={proxyAuth.saving} class="self-start">
					{proxyAuth.saving ? 'Saving…' : 'Save'}
				</Button>
			</form>
		</Card.Content>
	</Card.Root>
{/if}
