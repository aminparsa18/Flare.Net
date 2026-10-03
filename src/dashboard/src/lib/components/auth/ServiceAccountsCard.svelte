<script lang="ts">
	// Admin-only section of /auth (ADR-0082). Role and enable/disable for a service account
	// are already in UserTable (it lists every provider); this card adds creation plus
	// Admin-issued tokens, which are the only way a service account gets a credential.
	import * as Card from '$lib/components/ui/card';
	import * as Table from '$lib/components/ui/table';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { usersContext } from '$lib/users/context';
	import type { UserRole } from '$lib/auth-api';
	import type { AccessToken } from '$lib/personal-access-tokens-api';
	import { revokeAccessToken } from '$lib/personal-access-tokens-api';
	import { createServiceAccount, createServiceAccountToken, listServiceAccountTokens } from '$lib/service-accounts-api';
	import { formatDateTime } from '$lib/time/format';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import CheckIcon from '@lucide/svelte/icons/check';
	import * as m from '$lib/paraglide/messages';

	const users = usersContext.get();
	const ROLES: UserRole[] = ['Viewer', 'Member', 'Admin'];

	const accounts = $derived(users.users.filter((u) => u.authProvider === 'ServiceAccount'));

	let name = $state('');
	let role = $state<UserRole>('Viewer');
	let creating = $state(false);
	let error = $state<string | null>(null);

	let openId = $state<string | null>(null);
	let tokens = $state.raw<AccessToken[]>([]);
	let tokensLoading = $state(false);
	let tokenName = $state('');
	let expiresInDays = $state<number | null>(90);
	let revealed = $state<string | null>(null);
	let copied = $state(false);

	function roleLabel(r: UserRole): string {
		return r === 'Admin' ? m.userRole_admin() : r === 'Member' ? m.userRole_member() : m.userRole_viewer();
	}

	async function guard(action: () => Promise<void>): Promise<void> {
		error = null;
		try {
			await action();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function handleCreate(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		creating = true;
		await guard(async () => {
			await createServiceAccount(name.trim(), role);
			name = '';
			await users.load();
		});
		creating = false;
	}

	async function toggleTokens(id: string): Promise<void> {
		revealed = null;
		if (openId === id) {
			openId = null;
			return;
		}
		openId = id;
		tokenName = '';
		tokensLoading = true;
		await guard(async () => {
			tokens = await listServiceAccountTokens(id);
		});
		tokensLoading = false;
	}

	async function handleNewToken(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		if (!openId) return;
		const id = openId;
		await guard(async () => {
			const created = await createServiceAccountToken(id, tokenName.trim(), expiresInDays);
			revealed = created.rawToken;
			copied = false;
			tokenName = '';
			tokens = await listServiceAccountTokens(id);
		});
	}

	async function handleRevoke(token: AccessToken): Promise<void> {
		if (!openId || !confirm(m.accessTokenTable_confirmRevoke({ name: token.name }))) return;
		const id = openId;
		await guard(async () => {
			await revokeAccessToken(token.id);
			tokens = await listServiceAccountTokens(id);
		});
	}

	async function handleCopy(): Promise<void> {
		if (!revealed) return;
		await navigator.clipboard.writeText(revealed);
		copied = true;
		setTimeout(() => (copied = false), 1500);
	}
</script>

<Card.Root class="shrink-0">
	<Card.Header>
		<Card.Title>{m.serviceAccounts_title()}</Card.Title>
		<Card.Description>{m.serviceAccounts_description()}</Card.Description>
	</Card.Header>
	<Card.Content class="space-y-4">
		{#if error}
			<Alert variant="destructive">
				<AlertDescription>{error}</AlertDescription>
			</Alert>
		{/if}

		<form class="flex flex-wrap items-center gap-2" onsubmit={handleCreate}>
			<Input bind:value={name} required maxlength={64} placeholder={m.serviceAccounts_namePlaceholder()} class="w-56" />
			<Select.Root type="single" value={role} onValueChange={(v) => v && (role = v as UserRole)}>
				<Select.Trigger class="w-28">{roleLabel(role)}</Select.Trigger>
				<Select.Content>
					{#each ROLES as r (r)}
						<Select.Item value={r} label={roleLabel(r)} />
					{/each}
				</Select.Content>
			</Select.Root>
			<Button type="submit" size="sm" disabled={creating || !name.trim()}>
				{#if creating}<Spinner class="size-4" />{/if}
				{m.serviceAccounts_create()}
			</Button>
		</form>

		{#if accounts.length === 0}
			<p class="text-muted-foreground text-sm">{m.serviceAccounts_empty()}</p>
		{:else}
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.userTable_colUsername()}</Table.Head>
						<Table.Head>{m.userTable_colRole()}</Table.Head>
						<Table.Head>{m.userTable_colStatus()}</Table.Head>
						<Table.Head class="text-right"></Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each accounts as account (account.id)}
						<Table.Row>
							<Table.Cell class="font-medium">{account.username}</Table.Cell>
							<Table.Cell>{roleLabel(account.role)}</Table.Cell>
							<Table.Cell>
								<Badge variant={account.isDisabled ? 'destructive' : 'secondary'}
									>{account.isDisabled ? m.userTable_statusDisabled() : m.userTable_statusActive()}</Badge
								>
							</Table.Cell>
							<Table.Cell class="text-right">
								<Button variant="outline" size="sm" onclick={() => toggleTokens(account.id)}>{m.serviceAccounts_tokens()}</Button>
							</Table.Cell>
						</Table.Row>
						{#if openId === account.id}
							<Table.Row>
								<Table.Cell colspan={4} class="bg-muted/30 space-y-3">
									<form class="flex flex-wrap items-center gap-2" onsubmit={handleNewToken}>
										<Input bind:value={tokenName} required placeholder={m.createAccessTokenDialog_namePlaceholder()} class="w-48" />
										<label class="flex items-center gap-2 text-sm">
											{m.createAccessTokenDialog_expiryCustomLabel()}
											<Input
												type="number"
												min="1"
												max="365"
												class="w-20"
												value={expiresInDays ?? ''}
												placeholder="∞"
												oninput={(e) => {
													const v = e.currentTarget.value;
													expiresInDays = v === '' ? null : Number(v);
												}}
											/>
										</label>
										<Button type="submit" size="sm" disabled={!tokenName.trim() || account.isDisabled}>{m.createAccessTokenDialog_create()}</Button>
									</form>
									{#if revealed}
										<div class="flex items-center gap-2">
											<code class="bg-background flex-1 overflow-x-auto rounded-md border px-3 py-2 text-xs break-all">{revealed}</code>
											<Button type="button" variant="outline" size="icon" onclick={handleCopy} title={m.createAccessTokenDialog_copy()}>
												{#if copied}<CheckIcon />{:else}<CopyIcon />{/if}
											</Button>
										</div>
										<p class="text-muted-foreground text-xs">{m.createAccessTokenDialog_revealWarning()}</p>
									{/if}
									{#if tokensLoading}
										<Spinner />
									{:else if tokens.length === 0}
										<p class="text-muted-foreground text-sm">{m.serviceAccounts_noTokens()}</p>
									{:else}
										<ul class="space-y-1 text-sm">
											{#each tokens as token (token.id)}
												<li class="flex items-center justify-between gap-2">
													<span>
														{token.name}
														<span class="text-muted-foreground text-xs">
															· {formatDateTime(token.createdAt)}{token.expiresAt ? ` → ${formatDateTime(token.expiresAt)}` : ''}
														</span>
														{#if token.revokedAt}
															<Badge variant="destructive">{m.accessTokenTable_statusRevoked()}</Badge>
														{:else if !token.isActive}
															<Badge variant="warning">{m.accessTokenTable_statusExpired()}</Badge>
														{/if}
													</span>
													{#if token.isActive}
														<Button variant="ghost" size="sm" onclick={() => handleRevoke(token)}>{m.serviceAccounts_revoke()}</Button>
													{/if}
												</li>
											{/each}
										</ul>
									{/if}
								</Table.Cell>
							</Table.Row>
						{/if}
					{/each}
				</Table.Body>
			</Table.Root>
		{/if}
	</Card.Content>
</Card.Root>
