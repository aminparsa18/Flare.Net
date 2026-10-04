<script lang="ts">
	// One section of the consolidated /auth page - the first caller of Flare.Api's
	// /api/users endpoints (UserEndpoints.cs), which themselves wrap IUserStore methods
	// that existed since v11 but had no UI/API surface until this feature (see
	// docs/auth.md's "Managing users" section for why Entra auto-provisioning is what
	// forced this gap closed). Table/Select/Switch/Badge usage mirrors
	// AlertRuleTable.svelte/AlertRuleFormDialog.svelte's own precedent for these
	// components. Formerly its own page at /users - moved here, wrapped in a Card for
	// visual consistency with this page's other sections, when /security and /users
	// were consolidated into /auth.
	import * as Card from '$lib/components/ui/card';
	import * as Table from '$lib/components/ui/table';
	import * as Select from '$lib/components/ui/select';
	import { Switch } from '$lib/components/ui/switch';
	import { Badge } from '$lib/components/ui/badge';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { withBase } from '$lib/paths';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { usersContext } from '$lib/users/context';
	import { authContext } from '$lib/auth/context';
	import type { UserRole } from '$lib/auth-api';
	import * as m from '$lib/paraglide/messages';

	const users = usersContext.get();
	const auth = authContext.get();

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

	let inviteOpen = $state(false);
	let inviteUsername = $state('');
	let inviteRole = $state<UserRole>('Viewer');
	let copied = $state(false);

	async function submitInvite(e: SubmitEvent) {
		e.preventDefault();
		// One name per line (or comma/semicolon/space separated): several go through the bulk endpoint.
		const names = inviteUsername.split(/[\s,;]+/).filter(Boolean);
		if (names.length === 0) return;
		const ok = names.length === 1 ? await users.invite(names[0], inviteRole) : await users.inviteMany(names, inviteRole);
		if (ok) {
			inviteOpen = false;
			inviteUsername = '';
		}
	}

	function linkFor(token: string): string {
		return `${window.location.origin}${withBase('/set-password')}?token=${encodeURIComponent(token)}`;
	}

	async function copyAllLinks() {
		const lines = (users.bulkResult?.results ?? []).filter((r) => r.token).map((r) => `${r.username}\t${linkFor(r.token!)}`);
		await navigator.clipboard.writeText(lines.join('\n'));
		copied = true;
		setTimeout(() => (copied = false), 2000);
	}

	function statusLabel(status: string): string {
		return status === 'Created' ? m.userInvite_bulkCreated() : status === 'Exists' ? m.userInvite_bulkExists() : m.userInvite_bulkInvalid();
	}

	const linkUrl = $derived(
		users.issuedLink ? `${window.location.origin}${withBase('/set-password')}?token=${encodeURIComponent(users.issuedLink.token)}` : ''
	);

	async function copyLink() {
		await navigator.clipboard.writeText(linkUrl);
		copied = true;
		setTimeout(() => (copied = false), 2000);
	}
</script>

<Card.Root class="shrink-0">
	<Card.Header>
		<Card.Title>{m.userTable_title()}</Card.Title>
		<Card.Description>{m.userTable_description()}</Card.Description>
		<Card.Action>
			<Button size="sm" onclick={() => ((inviteOpen = true), (users.inviteError = null))}>{m.userTable_invite()}</Button>
		</Card.Action>
	</Card.Header>
	<Card.Content>
		{#if users.saveError}
			<Alert variant="destructive" class="mb-3">
				<AlertDescription>{users.saveError}</AlertDescription>
			</Alert>
		{/if}

		{#if users.loading}
			<div class="flex justify-center py-4">
				<Spinner />
			</div>
		{:else if users.error}
			<p class="text-destructive text-sm">{users.error}</p>
		{:else}
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.userTable_colUsername()}</Table.Head>
						<Table.Head>{m.userTable_colProvider()}</Table.Head>
						<Table.Head>{m.userTable_colRole()}</Table.Head>
						<Table.Head>{m.userTable_colStatus()}</Table.Head>
						<Table.Head class="text-right">{m.userTable_colEnabled()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each users.users as user (user.id)}
						{@const isSaving = users.savingId === user.id}
						<Table.Row>
							<Table.Cell class="font-medium">
								{user.username}
								{#if user.id === auth.currentUser?.id}
									<Badge variant="outline" class="ml-1">{m.userTable_youBadge()}</Badge>
								{/if}
							</Table.Cell>
							<Table.Cell>
								<Badge variant={user.authProvider === 'Local' ? 'outline' : 'secondary'}>{user.authProvider}</Badge>
							</Table.Cell>
							<Table.Cell>
								<Select.Root
									type="single"
									value={user.role}
									disabled={isSaving}
									onValueChange={(v) => v && users.changeRole(user, v as UserRole)}
								>
									<Select.Trigger class="w-28">
										{roleLabel(user.role)}
									</Select.Trigger>
									<Select.Content>
										{#each ROLES as role (role)}
											<Select.Item value={role} label={roleLabel(role)} />
										{/each}
									</Select.Content>
								</Select.Root>
							</Table.Cell>
							<Table.Cell>
								<Badge variant={user.isDisabled ? 'destructive' : 'secondary'}
									>{user.isDisabled ? m.userTable_statusDisabled() : m.userTable_statusActive()}</Badge
								>
							</Table.Cell>
							<Table.Cell class="text-right">
								{#if user.authProvider === 'Local'}
									<Button variant="ghost" size="sm" disabled={isSaving} onclick={() => users.issueResetLink(user)}>
										{m.userTable_resetLink()}
									</Button>
								{/if}
								<Switch
									checked={!user.isDisabled}
									disabled={isSaving}
									onCheckedChange={(checked) => users.toggleDisabled(user, !checked)}
								/>
							</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		{/if}
	</Card.Content>
</Card.Root>

<Dialog.Root bind:open={inviteOpen}>
	<Dialog.Content>
		<form class="flex flex-col gap-3" onsubmit={submitInvite}>
			<Dialog.Header>
				<Dialog.Title>{m.userInvite_title()}</Dialog.Title>
				<Dialog.Description>{m.userInvite_description()}</Dialog.Description>
			</Dialog.Header>
			{#if users.inviteError}
				<Alert variant="destructive"><AlertDescription>{users.inviteError}</AlertDescription></Alert>
			{/if}
			<textarea
				class="border-input bg-background min-h-20 rounded-md border px-3 py-2 text-sm"
				placeholder={m.userInvite_username()}
				bind:value={inviteUsername}
				autocomplete="off"
				rows="3"
			></textarea>
			<p class="text-muted-foreground text-xs">{m.userInvite_bulkHint()}</p>
			<Select.Root type="single" bind:value={inviteRole}>
				<Select.Trigger class="w-full">{m.userInvite_role()}: {roleLabel(inviteRole)}</Select.Trigger>
				<Select.Content>
					{#each ROLES as role (role)}
						<Select.Item value={role} label={roleLabel(role)} />
					{/each}
				</Select.Content>
			</Select.Root>
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => (inviteOpen = false)}>{m.userInvite_cancel()}</Button>
				<Button type="submit">{m.userInvite_create()}</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root open={users.issuedLink !== null} onOpenChange={(o) => !o && (users.issuedLink = null)}>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.userInvite_linkTitle({ username: users.issuedLink?.user.username ?? '' })}</Dialog.Title>
			<Dialog.Description>
				{m.userInvite_linkHint({ expires: users.issuedLink ? new Date(users.issuedLink.expiresAt).toLocaleString() : '' })}
			</Dialog.Description>
		</Dialog.Header>
		{#if users.issuedLink?.emailSent}
			<p class="text-muted-foreground text-sm">{m.userInvite_emailed({ username: users.issuedLink.user.username })}</p>
		{/if}
		<Input readonly value={linkUrl} onfocus={(e) => e.currentTarget.select()} />
		<Dialog.Footer>
			<Button variant="outline" onclick={copyLink}>{copied ? m.userInvite_copied() : m.userInvite_copy()}</Button>
			<Button onclick={() => (users.issuedLink = null)}>{m.userInvite_done()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root open={users.bulkResult !== null} onOpenChange={(o) => !o && (users.bulkResult = null)}>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.userInvite_bulkTitle()}</Dialog.Title>
			<Dialog.Description>
				{m.userInvite_bulkHintResult({ expires: users.bulkResult ? new Date(users.bulkResult.expiresAt).toLocaleString() : '' })}
			</Dialog.Description>
		</Dialog.Header>
		<ul class="max-h-72 divide-y overflow-auto text-sm">
			{#each users.bulkResult?.results ?? [] as r (r.username)}
				<li class="flex items-center justify-between gap-2 py-1.5">
					<span class="truncate">{r.username}</span>
					<span class="text-muted-foreground shrink-0 text-xs">
						{statusLabel(r.status)}{r.emailSent ? ` · ${m.userInvite_bulkEmailed()}` : ''}
					</span>
				</li>
			{/each}
		</ul>
		<Dialog.Footer>
			<Button variant="outline" onclick={copyAllLinks}>{copied ? m.userInvite_copied() : m.userInvite_bulkCopyAll()}</Button>
			<Button onclick={() => (users.bulkResult = null)}>{m.userInvite_done()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
