<script lang="ts">
	// Mirrors AlertRuleTable.svelte's header-button + Table/Empty/Badge composition -
	// same "New X" button both above the table and inside the empty state.
	import * as Table from '#lib/components/ui/table/index.ts';
	import * as Empty from '#lib/components/ui/empty/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge, type BadgeVariant } from '#lib/components/ui/badge/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { accessTokensContext } from '#lib/access-tokens/context.ts';
	import type { AccessToken } from '#lib/personal-access-tokens-api.ts';
	import KeyRoundIcon from '@lucide/svelte/icons/key-round';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '#lib/paraglide/messages.js';
	import { formatDateTime } from '#lib/time/format.ts';

	const tokens = accessTokensContext.get();

	const SOON_DAYS = 14;

	type Status = 'active' | 'expiring' | 'expired' | 'revoked';

	function daysLeft(expiresAt: string): number {
		return Math.max(0, Math.ceil((new Date(expiresAt).getTime() - Date.now()) / 86_400_000));
	}

	// RevokedAt wins over an expired ExpiresAt if somehow both are true (can't happen
	// today - revoking doesn't touch ExpiresAt - but this keeps the precedence explicit
	// rather than accidental). Mirrors PersonalAccessToken.IsActive's own
	// RevokedAt-first check on the backend.
	function status(token: AccessToken): Status {
		if (token.revokedAt) return 'revoked';
		if (!token.isActive) return 'expired';
		return token.expiresAt && daysLeft(token.expiresAt) <= SOON_DAYS ? 'expiring' : 'active';
	}

	function statusLabel(status: Status, expiresAt?: string | null): string {
		switch (status) {
			case 'active':
				return m.accessTokenTable_statusActive();
			case 'expiring':
				return m.accessTokenTable_expiresSoon({ days: daysLeft(expiresAt!) });
			case 'expired':
				return m.accessTokenTable_statusExpired();
			case 'revoked':
				return m.accessTokenTable_statusRevoked();
		}
	}

	function statusVariant(status: Status): BadgeVariant {
		switch (status) {
			case 'active':
				return 'outline';
			case 'expiring':
			case 'expired':
				return 'warning';
			case 'revoked':
				return 'destructive';
		}
	}

	async function handleRevoke(token: AccessToken): Promise<void> {
		if (!confirm(m.accessTokenTable_confirmRevoke({ name: token.name }))) return;
		await tokens.revoke(token.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.accessTokenTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.accessTokenTable_description()}</p>
	</div>
	<Button size="sm" onclick={() => tokens.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.accessTokenTable_newToken()}
	</Button>
</div>

{#if tokens.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if tokens.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{tokens.error}</p>
	</div>
{:else if tokens.tokens.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<KeyRoundIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.accessTokenTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.accessTokenTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => tokens.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.accessTokenTable_newToken()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.accessTokenTable_nameColumn()}</Table.Head>
					<Table.Head>{m.accessTokenTable_statusColumn()}</Table.Head>
					<Table.Head>{m.accessTokenTable_createdColumn()}</Table.Head>
					<Table.Head>{m.accessTokenTable_expiresColumn()}</Table.Head>
					<Table.Head>{m.accessTokenTable_lastUsedColumn()}</Table.Head>
					<Table.Head class="text-right">{m.accessTokenTable_actionsColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each tokens.tokens as token (token.id)}
					{@const tokenStatus = status(token)}
					<Table.Row>
						<Table.Cell class="font-medium">{token.name}</Table.Cell>
						<Table.Cell><Badge variant={statusVariant(tokenStatus)}>{statusLabel(tokenStatus, token.expiresAt)}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground">{formatDateTime(token.createdAt)}</Table.Cell>
						<Table.Cell class="text-muted-foreground">
							{token.expiresAt ? formatDateTime(token.expiresAt) : m.accessTokenTable_neverExpires()}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">
							{token.lastUsedAt ? formatDateTime(token.lastUsedAt) : m.accessTokenTable_neverUsed()}
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.accessTokenTable_revoke()}
								disabled={tokenStatus === 'revoked'}
								onclick={() => handleRevoke(token)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}
