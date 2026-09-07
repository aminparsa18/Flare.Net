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
</script>

<Card.Root class="shrink-0">
	<Card.Header>
		<Card.Title>{m.userTable_title()}</Card.Title>
		<Card.Description>{m.userTable_description()}</Card.Description>
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
