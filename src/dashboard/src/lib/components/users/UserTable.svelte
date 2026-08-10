<script lang="ts">
	// Admin-only "manage users" screen - the first caller of Flare.Api's /api/users
	// endpoints (UserEndpoints.cs), which themselves wrap IUserStore methods that existed
	// since v11 but had no UI/API surface until this feature (see docs/auth.md's
	// "Managing users" section for why Entra auto-provisioning is what forced this gap
	// closed). Table/Select/Switch/Badge usage mirrors AlertRuleTable.svelte/
	// AlertRuleFormDialog.svelte's own precedent for these components.
	import * as Table from '$lib/components/ui/table';
	import * as Select from '$lib/components/ui/select';
	import { Switch } from '$lib/components/ui/switch';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { usersContext } from '$lib/users/context';
	import { authContext } from '$lib/auth/context';
	import type { UserRole } from '$lib/auth-api';

	const users = usersContext.get();
	const auth = authContext.get();

	const ROLES: UserRole[] = ['Admin', 'Member', 'Viewer'];
</script>

<div class="border-b px-4 py-3">
	<h1 class="text-sm font-semibold">Users</h1>
	<p class="text-muted-foreground text-xs">Manage accounts and roles - local and Microsoft Entra ID (SSO) alike.</p>
</div>

{#if users.saveError}
	<Alert variant="destructive" class="m-4 w-auto">
		<AlertDescription>{users.saveError}</AlertDescription>
	</Alert>
{/if}

{#if users.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if users.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{users.error}</p>
	</div>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>Username</Table.Head>
					<Table.Head>Provider</Table.Head>
					<Table.Head>Role</Table.Head>
					<Table.Head>Status</Table.Head>
					<Table.Head class="text-right">Enabled</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each users.users as user (user.id)}
					{@const isSaving = users.savingId === user.id}
					<Table.Row>
						<Table.Cell class="font-medium">
							{user.username}
							{#if user.id === auth.currentUser?.id}
								<Badge variant="outline" class="ml-1">You</Badge>
							{/if}
						</Table.Cell>
						<Table.Cell>
							<Badge variant={user.authProvider === 'Entra' ? 'secondary' : 'outline'}>{user.authProvider}</Badge>
						</Table.Cell>
						<Table.Cell>
							<Select.Root
								type="single"
								value={user.role}
								disabled={isSaving}
								onValueChange={(v) => v && users.changeRole(user, v as UserRole)}
							>
								<Select.Trigger class="w-28">
									{user.role}
								</Select.Trigger>
								<Select.Content>
									{#each ROLES as role (role)}
										<Select.Item value={role} label={role} />
									{/each}
								</Select.Content>
							</Select.Root>
						</Table.Cell>
						<Table.Cell>
							<Badge variant={user.isDisabled ? 'destructive' : 'secondary'}>{user.isDisabled ? 'Disabled' : 'Active'}</Badge>
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
	</div>
{/if}
