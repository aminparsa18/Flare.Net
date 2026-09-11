<script lang="ts">
	// Mirrors AlertRuleTable.svelte's header-button + Table/Empty/Badge composition -
	// same "New X" button both above the table and inside the empty state.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge, type BadgeVariant } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { accessTokensContext } from '$lib/access-tokens/context';
	import type { AccessToken } from '$lib/personal-access-tokens-api';
	import KeyRoundIcon from '@lucide/svelte/icons/key-round';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	const tokens = accessTokensContext.get();

	function formatDate(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	type Status = 'active' | 'expired' | 'revoked';

	// RevokedAt wins over an expired ExpiresAt if somehow both are true (can't happen
	// today - revoking doesn't touch ExpiresAt - but this keeps the precedence explicit
	// rather than accidental). Mirrors PersonalAccessToken.IsActive's own
	// RevokedAt-first check on the backend.
	function status(token: AccessToken): Status {
		if (token.revokedAt) return 'revoked';
		return token.isActive ? 'active' : 'expired';
	}

	function statusLabel(status: Status): string {
		switch (status) {
			case 'active':
				return m.accessTokenTable_statusActive();
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
						<Table.Cell><Badge variant={statusVariant(tokenStatus)}>{statusLabel(tokenStatus)}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground">{formatDate(token.createdAt)}</Table.Cell>
						<Table.Cell class="text-muted-foreground">
							{token.expiresAt ? formatDate(token.expiresAt) : m.accessTokenTable_neverExpires()}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">
							{token.lastUsedAt ? formatDate(token.lastUsedAt) : m.accessTokenTable_neverUsed()}
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
