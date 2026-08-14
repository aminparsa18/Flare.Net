<script lang="ts">
	// The consolidated auth screen (replaces the old separate /security and /users
	// pages, per the user's own steer - see Planning.md): enable/disable auth
	// altogether, configure whichever sign-in methods, and manage existing accounts,
	// all in one place. Open to anyone while auth is off (+layout.svelte's route guard
	// only Admin-gates this once auth.authEnabled is true - see its remarks), since
	// nobody has a role to check yet in that state.
	import { onMount } from 'svelte';
	import { AuthSettingsState } from '$lib/auth-settings/state.svelte';
	import { authSettingsContext } from '$lib/auth-settings/context';
	import { EntraSettingsState } from '$lib/entra-settings/state.svelte';
	import { entraSettingsContext } from '$lib/entra-settings/context';
	import { LdapSettingsState } from '$lib/ldap-settings/state.svelte';
	import { ldapSettingsContext } from '$lib/ldap-settings/context';
	import { UsersState } from '$lib/users/state.svelte';
	import { usersContext } from '$lib/users/context';
	import AuthToggleCard from '$lib/components/auth/AuthToggleCard.svelte';
	import EntraSecurityForm from '$lib/components/auth/EntraSecurityForm.svelte';
	import LdapSecurityForm from '$lib/components/auth/LdapSecurityForm.svelte';
	import UserTable from '$lib/components/auth/UserTable.svelte';

	const authSettings = authSettingsContext.set(new AuthSettingsState());
	const entraSettings = entraSettingsContext.set(new EntraSettingsState());
	const ldapSettings = ldapSettingsContext.set(new LdapSettingsState());
	const users = usersContext.set(new UsersState());

	onMount(() => {
		void authSettings.load();
		void entraSettings.load();
		void ldapSettings.load();
		void users.load();
	});
</script>

<svelte:head>
	<title>Flare — Auth</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="border-b px-4 py-3">
		<h1 class="text-sm font-semibold">Auth</h1>
		<p class="text-muted-foreground text-xs">Sign-in methods and account management for this Flare instance.</p>
	</div>
	<div class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-4">
		<AuthToggleCard />
		<EntraSecurityForm />
		<LdapSecurityForm />
		<UserTable />
	</div>
</div>
