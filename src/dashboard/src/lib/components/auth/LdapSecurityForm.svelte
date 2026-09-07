<script lang="ts">
	// One section of the consolidated /auth page - lets each self-hosted Flare operator
	// point Flare at their own Active Directory (or AD-compatible) directory over
	// LDAP/LDAPS, mirroring EntraSecurityForm.svelte's shape. Unlike Entra, saved values
	// take effect immediately - no restart needed (see LdapAuthEndpoints' remarks) - so
	// the post-save banner just says "Saved," not "restart to apply."
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Switch } from '$lib/components/ui/switch';
	import * as Select from '$lib/components/ui/select';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import { ldapSettingsContext } from '$lib/ldap-settings/context';
	import type { UserRole } from '$lib/auth-api';
	import * as m from '$lib/paraglide/messages';

	const ldap = ldapSettingsContext.get();

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
	let host = $state('');
	let port = $state(636);
	let useSsl = $state(true);
	let pinnedCertificatePem = $state('');
	let baseDn = $state('');
	let bindDn = $state('');
	let bindPassword = $state('');
	let userSearchFilter = $state('');
	let uniqueIdAttribute = $state('');
	let adminGroupDn = $state('');
	let memberGroupDn = $state('');
	let viewerGroupDn = $state('');
	let defaultRole = $state<UserRole>('Viewer');

	// Re-seeds the form whenever ldap.settings changes identity - on initial load, and
	// again after a successful save - same "seed once per data change" shape
	// EntraSecurityForm.svelte's own effect already establishes.
	$effect(() => {
		if (ldap.settings) {
			enabled = ldap.settings.enabled;
			host = ldap.settings.host ?? '';
			port = ldap.settings.port;
			useSsl = ldap.settings.useSsl;
			pinnedCertificatePem = ldap.settings.pinnedCertificatePem ?? '';
			baseDn = ldap.settings.baseDn ?? '';
			bindDn = ldap.settings.bindDn ?? '';
			bindPassword = '';
			userSearchFilter = ldap.settings.userSearchFilter;
			uniqueIdAttribute = ldap.settings.uniqueIdAttribute;
			adminGroupDn = ldap.settings.adminGroupDn ?? '';
			memberGroupDn = ldap.settings.memberGroupDn ?? '';
			viewerGroupDn = ldap.settings.viewerGroupDn ?? '';
			defaultRole = ldap.settings.defaultRole;
		}
	});

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await ldap.save({
			enabled,
			host: host.trim() || null,
			port,
			useSsl,
			// Blank clears the pin (reverts to the OS/container trust store) - unlike
			// bindPassword below, there's no "leave unchanged" semantics here.
			pinnedCertificatePem: pinnedCertificatePem.trim() || null,
			baseDn: baseDn.trim() || null,
			bindDn: bindDn.trim() || null,
			// Blank means "leave whatever's already stored unchanged" - only send a real
			// value when the Admin actually typed a new password.
			bindPassword: bindPassword.trim() || null,
			userSearchFilter: userSearchFilter.trim(),
			uniqueIdAttribute: uniqueIdAttribute.trim(),
			adminGroupDn: adminGroupDn.trim() || null,
			memberGroupDn: memberGroupDn.trim() || null,
			viewerGroupDn: viewerGroupDn.trim() || null,
			defaultRole
		});
	}
</script>

{#if ldap.loading}
	<div class="flex items-center justify-center py-12">
		<Spinner />
	</div>
{:else if ldap.error}
	<p class="text-destructive text-sm">{ldap.error}</p>
{:else if ldap.settings}
	<Card.Root class="shrink-0">
		<Card.Header>
			<Card.Title>{m.ldapSecurityForm_title()}</Card.Title>
			<Card.Description>{m.ldapSecurityForm_description()}</Card.Description>
		</Card.Header>
		<Card.Content>
			<form class="flex flex-col gap-4" onsubmit={handleSubmit}>
				{#if ldap.saveError}
					<Alert variant="destructive">
						<AlertDescription>{ldap.saveError}</AlertDescription>
					</Alert>
				{/if}
				{#if ldap.justSaved}
					<Alert>
						<AlertDescription>{m.ldapSecurityForm_saved()}</AlertDescription>
					</Alert>
				{/if}

				<div class="flex gap-2">
					<div class="flex flex-1 flex-col gap-1">
						<label for="ldap-host" class="text-xs font-medium">{m.ldapSecurityForm_hostLabel()}</label>
						<Input id="ldap-host" bind:value={host} placeholder="dc.corp.example.com" />
					</div>
					<div class="flex w-20 flex-col gap-1">
						<label for="ldap-port" class="text-xs font-medium">{m.ldapSecurityForm_portLabel()}</label>
						<Input id="ldap-port" type="number" min="1" max="65535" bind:value={port} />
					</div>
				</div>

				<div class="flex items-center gap-2">
					<Switch bind:checked={useSsl} />
					<span class="text-xs">{m.ldapSecurityForm_useSslLabel()}</span>
				</div>

				<div class="flex flex-col gap-1">
					<label for="ldap-pinned-cert" class="text-xs font-medium">{m.ldapSecurityForm_pinnedCertLabel()}</label>
					<Textarea
						id="ldap-pinned-cert"
						bind:value={pinnedCertificatePem}
						disabled={!useSsl}
						placeholder={'-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----'}
						rows={6}
						class="font-mono text-xs"
					/>
					<p class="text-muted-foreground text-xs">
						{@html m.ldapSecurityForm_pinnedCertHint({
							and: '<strong>and</strong>',
							only: '<strong>only</strong>',
							cmd: '<code>openssl s_client -connect {host}:{port} -showcerts</code>'
						})}
					</p>
				</div>

				<div class="flex flex-col gap-1">
					<label for="ldap-base-dn" class="text-xs font-medium">{m.ldapSecurityForm_baseDnLabel()}</label>
					<Input id="ldap-base-dn" bind:value={baseDn} placeholder="DC=corp,DC=example,DC=com" />
				</div>

				<div class="flex flex-col gap-1">
					<label for="ldap-bind-dn" class="text-xs font-medium">{m.ldapSecurityForm_bindDnLabel()}</label>
					<Input id="ldap-bind-dn" bind:value={bindDn} placeholder="CN=flare-svc,OU=Service Accounts,DC=corp,DC=example,DC=com" />
				</div>

				<div class="flex flex-col gap-1">
					<label for="ldap-bind-password" class="text-xs font-medium">{m.ldapSecurityForm_bindPasswordLabel()}</label>
					<Input
						id="ldap-bind-password"
						type="password"
						bind:value={bindPassword}
						placeholder={ldap.settings.hasBindPassword
							? m.ldapSecurityForm_bindPasswordPlaceholderUnchanged()
							: m.ldapSecurityForm_bindPasswordPlaceholderNew()}
					/>
					<p class="text-muted-foreground text-xs">{m.ldapSecurityForm_bindPasswordHint()}</p>
				</div>

				<div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
					<div class="flex flex-col gap-1">
						<label for="ldap-admin-group" class="text-xs font-medium">{m.ldapSecurityForm_adminGroupLabel()}</label>
						<Input id="ldap-admin-group" bind:value={adminGroupDn} placeholder={m.ldapSecurityForm_optionalPlaceholder()} />
					</div>
					<div class="flex flex-col gap-1">
						<label for="ldap-member-group" class="text-xs font-medium">{m.ldapSecurityForm_memberGroupLabel()}</label>
						<Input id="ldap-member-group" bind:value={memberGroupDn} placeholder={m.ldapSecurityForm_optionalPlaceholder()} />
					</div>
					<div class="flex flex-col gap-1">
						<label for="ldap-viewer-group" class="text-xs font-medium">{m.ldapSecurityForm_viewerGroupLabel()}</label>
						<Input id="ldap-viewer-group" bind:value={viewerGroupDn} placeholder={m.ldapSecurityForm_optionalPlaceholder()} />
					</div>
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.ldapSecurityForm_defaultRoleLabel()}</span>
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
					<p class="text-muted-foreground text-xs">{m.ldapSecurityForm_defaultRoleHint()}</p>
				</div>

				<details class="text-xs">
					<summary class="text-muted-foreground cursor-pointer font-medium">{m.ldapSecurityForm_advanced()}</summary>
					<div class="mt-3 flex flex-col gap-3">
						<div class="flex flex-col gap-1">
							<label for="ldap-user-filter" class="text-xs font-medium">{m.ldapSecurityForm_userFilterLabel()}</label>
							<Input id="ldap-user-filter" bind:value={userSearchFilter} class="font-mono text-xs" />
							<p class="text-muted-foreground text-xs">
								{@html m.ldapSecurityForm_userFilterHint({ placeholder: '<code>{0}</code>' })}
							</p>
						</div>
						<div class="flex flex-col gap-1">
							<label for="ldap-unique-id" class="text-xs font-medium">{m.ldapSecurityForm_uniqueIdLabel()}</label>
							<Input id="ldap-unique-id" bind:value={uniqueIdAttribute} class="font-mono text-xs" />
							<p class="text-muted-foreground text-xs">
								{@html m.ldapSecurityForm_uniqueIdHint({ code: '<code>objectGUID</code>' })}
							</p>
						</div>
					</div>
				</details>

				<div class="flex items-center gap-2">
					<Switch bind:checked={enabled} />
					<span class="text-xs">{m.ldapSecurityForm_enabled()}</span>
				</div>

				<Button type="submit" disabled={ldap.saving} class="self-start">
					{ldap.saving ? m.ldapSecurityForm_saving() : m.ldapSecurityForm_save()}
				</Button>
			</form>
		</Card.Content>
	</Card.Root>
{/if}
