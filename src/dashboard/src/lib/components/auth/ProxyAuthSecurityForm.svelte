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
	import * as m from '$lib/paraglide/messages';

	const proxyAuth = proxyAuthSettingsContext.get();

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
			<Card.Title>{m.proxyAuthSecurityForm_title()}</Card.Title>
			<Card.Description>{m.proxyAuthSecurityForm_description()}</Card.Description>
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
						<AlertDescription>{m.proxyAuthSecurityForm_saved()}</AlertDescription>
					</Alert>
				{/if}

				<div class="flex flex-col gap-1">
					<label for="proxy-header-name" class="text-xs font-medium">{m.proxyAuthSecurityForm_headerNameLabel()}</label>
					<Input id="proxy-header-name" bind:value={headerName} placeholder="Remote-User" class="font-mono text-xs" />
					<p class="text-muted-foreground text-xs">{m.proxyAuthSecurityForm_headerNameHint()}</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="proxy-trusted-cidrs" class="text-xs font-medium">{m.proxyAuthSecurityForm_trustedCidrsLabel()}</label>
					<Textarea
						id="proxy-trusted-cidrs"
						bind:value={trustedProxyCidrs}
						placeholder={'172.18.0.0/16'}
						rows={3}
						class="font-mono text-xs"
					/>
					<p class="text-muted-foreground text-xs">
						{@html m.proxyAuthSecurityForm_trustedCidrsHint({ required: '<strong>required</strong>' })}
					</p>
				</div>

				<details class="text-xs">
					<summary class="text-muted-foreground cursor-pointer font-medium">{m.proxyAuthSecurityForm_advancedRoleMapping()}</summary>
					<div class="mt-3 flex flex-col gap-3">
						<div class="flex flex-col gap-1">
							<label for="proxy-groups-header" class="text-xs font-medium">{m.proxyAuthSecurityForm_groupsHeaderLabel()}</label>
							<Input
								id="proxy-groups-header"
								bind:value={groupsHeaderName}
								placeholder={m.proxyAuthSecurityForm_groupsHeaderPlaceholder()}
								class="font-mono text-xs"
							/>
							<p class="text-muted-foreground text-xs">{m.proxyAuthSecurityForm_groupsHeaderHint()}</p>
						</div>
						<div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
							<div class="flex flex-col gap-1">
								<label for="proxy-admin-group" class="text-xs font-medium">{m.proxyAuthSecurityForm_adminGroupLabel()}</label>
								<Input id="proxy-admin-group" bind:value={adminGroup} placeholder={m.proxyAuthSecurityForm_optionalPlaceholder()} />
							</div>
							<div class="flex flex-col gap-1">
								<label for="proxy-member-group" class="text-xs font-medium">{m.proxyAuthSecurityForm_memberGroupLabel()}</label>
								<Input id="proxy-member-group" bind:value={memberGroup} placeholder={m.proxyAuthSecurityForm_optionalPlaceholder()} />
							</div>
							<div class="flex flex-col gap-1">
								<label for="proxy-viewer-group" class="text-xs font-medium">{m.proxyAuthSecurityForm_viewerGroupLabel()}</label>
								<Input id="proxy-viewer-group" bind:value={viewerGroup} placeholder={m.proxyAuthSecurityForm_optionalPlaceholder()} />
							</div>
						</div>
					</div>
				</details>

				<details class="text-xs">
					<summary class="text-muted-foreground cursor-pointer font-medium">{m.proxyAuthSecurityForm_advancedLogout()}</summary>
					<div class="mt-3 flex flex-col gap-1">
						<label for="proxy-logout-redirect-url" class="text-xs font-medium">{m.proxyAuthSecurityForm_logoutRedirectLabel()}</label>
						<Input
							id="proxy-logout-redirect-url"
							bind:value={logoutRedirectUrl}
							placeholder={m.proxyAuthSecurityForm_logoutRedirectPlaceholder()}
							class="font-mono text-xs"
						/>
						<p class="text-muted-foreground text-xs">{m.proxyAuthSecurityForm_logoutRedirectHint()}</p>
					</div>
				</details>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.proxyAuthSecurityForm_defaultRoleLabel()}</span>
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
					<p class="text-muted-foreground text-xs">{m.proxyAuthSecurityForm_defaultRoleHint()}</p>
				</div>

				<div class="flex items-center gap-2">
					<Switch bind:checked={enabled} />
					<span class="text-xs">{m.proxyAuthSecurityForm_enabled()}</span>
				</div>

				<Button type="submit" disabled={proxyAuth.saving} class="self-start">
					{proxyAuth.saving ? m.proxyAuthSecurityForm_saving() : m.proxyAuthSecurityForm_save()}
				</Button>
			</form>
		</Card.Content>
	</Card.Root>
{/if}
