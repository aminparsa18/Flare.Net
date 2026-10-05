<script lang="ts">
	// Per-project membership (ADR-0123): who belongs to a project and with which role. The role
	// is independent of the user's global role and can only narrow it for project-owned objects.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import * as Table from '$lib/components/ui/table';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import {
		listProjectMembers,
		removeProjectMember,
		setProjectMember,
		type Project,
		type ProjectMember
	} from '$lib/projects-api';
	import { listUsers, type UserSummary } from '$lib/users-api';
	import type { UserRole } from '$lib/auth-api';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	let { target, onclose }: { target: Project | null; onclose: () => void } = $props();

	const ROLES: UserRole[] = ['Admin', 'Member', 'Viewer'];
	const roleLabel = (role: UserRole): string =>
		role === 'Admin' ? m.projectMembers_roleAdmin() : role === 'Member' ? m.projectMembers_roleMember() : m.projectMembers_roleViewer();

	let members = $state<ProjectMember[]>([]);
	let users = $state<UserSummary[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let addUserId = $state('');
	let addRole = $state<UserRole>('Member');

	const addable = $derived(users.filter((u) => !u.isDisabled && !members.some((x) => x.userId === u.id)));

	async function load(project: Project): Promise<void> {
		loading = true;
		error = null;
		try {
			[members, users] = await Promise.all([listProjectMembers(project.id), listUsers()]);
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		if (target) {
			addUserId = '';
			addRole = 'Member';
			void load(target);
		}
	});

	async function run(action: () => Promise<void>): Promise<void> {
		if (!target) return;
		error = null;
		try {
			await action();
			members = await listProjectMembers(target.id);
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	}

	async function add(): Promise<void> {
		if (!target || !addUserId) return;
		const id = target.id;
		const userId = addUserId;
		await run(() => setProjectMember(id, userId, addRole));
		addUserId = '';
	}
</script>

<Dialog.Root open={target !== null} onOpenChange={(next) => !next && onclose()}>
	<Dialog.Content class="sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.projectMembers_title({ name: target?.name ?? '' })}</Dialog.Title>
			<Dialog.Description>{m.projectMembers_description()}</Dialog.Description>
		</Dialog.Header>

		{#if loading}
			<div class="flex justify-center py-6"><Spinner /></div>
		{:else}
			{#if members.length === 0}
				<p class="text-muted-foreground text-sm">{m.projectMembers_empty()}</p>
			{:else}
				<Table.Root>
					<Table.Body>
						{#each members as member (member.userId)}
							<Table.Row>
								<Table.Cell class="font-medium">{member.username}</Table.Cell>
								<Table.Cell>
									<Select.Root
										type="single"
										value={member.role}
										onValueChange={(v) => v && v !== member.role && run(() => setProjectMember(target!.id, member.userId, v as UserRole))}
									>
										<Select.Trigger size="sm" class="w-32">{roleLabel(member.role)}</Select.Trigger>
										<Select.Content>
											{#each ROLES as role (role)}
												<Select.Item value={role} label={roleLabel(role)} />
											{/each}
										</Select.Content>
									</Select.Root>
								</Table.Cell>
								<Table.Cell class="text-right">
									<Button
										variant="ghost"
										size="icon-sm"
										class="text-destructive hover:text-destructive"
										title={m.projectMembers_remove()}
										onclick={() => run(() => removeProjectMember(target!.id, member.userId))}
									>
										<Trash2Icon />
									</Button>
								</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			{/if}

			<div class="flex items-end gap-2 border-t pt-3">
				<div class="flex flex-1 flex-col gap-1">
					<span class="text-xs font-medium">{m.projectMembers_addUser()}</span>
					<Select.Root type="single" value={addUserId} onValueChange={(v) => (addUserId = v ?? '')}>
						<Select.Trigger size="sm" class="w-full">
							{users.find((u) => u.id === addUserId)?.username ?? m.projectMembers_pickUser()}
						</Select.Trigger>
						<Select.Content>
							{#each addable as user (user.id)}
								<Select.Item value={user.id} label={user.username} />
							{/each}
						</Select.Content>
					</Select.Root>
				</div>
				<Select.Root type="single" value={addRole} onValueChange={(v) => v && (addRole = v as UserRole)}>
					<Select.Trigger size="sm" class="w-32">{roleLabel(addRole)}</Select.Trigger>
					<Select.Content>
						{#each ROLES as role (role)}
							<Select.Item value={role} label={roleLabel(role)} />
						{/each}
					</Select.Content>
				</Select.Root>
				<Button size="sm" disabled={!addUserId} onclick={add}>{m.projectMembers_add()}</Button>
			</div>
		{/if}
		{#if error}
			<p class="text-destructive text-sm">{error}</p>
		{/if}
	</Dialog.Content>
</Dialog.Root>
