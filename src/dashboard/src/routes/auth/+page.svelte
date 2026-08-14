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
	import { OidcSettingsState } from '$lib/oidc-settings/state.svelte';
	import { oidcSettingsContext } from '$lib/oidc-settings/context';
	import { UsersState } from '$lib/users/state.svelte';
	import { usersContext } from '$lib/users/context';
	import AuthToggleCard from '$lib/components/auth/AuthToggleCard.svelte';
	import EntraSecurityForm from '$lib/components/auth/EntraSecurityForm.svelte';
	import LdapSecurityForm from '$lib/components/auth/LdapSecurityForm.svelte';
	import OidcSecurityForm from '$lib/components/auth/OidcSecurityForm.svelte';
	import UserTable from '$lib/components/auth/UserTable.svelte';

	const authSettings = authSettingsContext.set(new AuthSettingsState());
	const entraSettings = entraSettingsContext.set(new EntraSettingsState());
	const ldapSettings = ldapSettingsContext.set(new LdapSettingsState());
	const oidcSettings = oidcSettingsContext.set(new OidcSettingsState());
	const users = usersContext.set(new UsersState());

	onMount(() => {
		void authSettings.load();
		void entraSettings.load();
		void ldapSettings.load();
		void oidcSettings.load();
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
	<div class="flex min-h-0 flex-1 flex-col overflow-y-auto p-4">
		<!-- mx-auto/max-w-5xl centers+caps the whole column instead of each card picking
		     its own mismatched max-width (the pre-grid layout's approach) - this also
		     sizes each method card's grid column to a sane form width instead of
		     stretching to whatever the viewport happens to be.

		     Every direct card below needs `shrink-0` (see AuthToggleCard.svelte etc.) -
		     shadcn's Card.Root sets `overflow-hidden` (for rounded-corner image clipping),
		     which under the CSS flexbox spec makes a flex item's *automatic* minimum size
		     resolve to 0 instead of its content size. Without `shrink-0`, once this
		     column's total content height exceeds the viewport, flexbox compresses each
		     card to fit instead of this container's own `overflow-y-auto` ever having
		     anything to scroll - the cards silently clip their own content instead of the
		     page scrolling to reveal it. Confirmed live: `scrollHeight === clientHeight`
		     on the scroll container with this bug present, i.e. the browser saw nothing to
		     scroll at all. Any future card added here needs the same `shrink-0`. -->
		<div class="mx-auto flex w-full max-w-5xl flex-col gap-4">
			<AuthToggleCard />
			<!-- Per-method sign-in config, two columns on wide viewports - was one long
			     single-file column, but that doesn't scale as more methods get added
			     alongside Entra/AD/generic OIDC. Falls back to one column below `xl` so
			     narrow/tablet widths don't squeeze forms side by side. -->
			<div class="grid grid-cols-1 gap-4 xl:grid-cols-2">
				<EntraSecurityForm />
				<LdapSecurityForm />
				<OidcSecurityForm />
			</div>
			<UserTable />
		</div>
	</div>
</div>
