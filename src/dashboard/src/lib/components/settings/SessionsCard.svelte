<script lang="ts">
	// Active sessions for the signed-in user, with per-session revoke and sign-out-everywhere.
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { authContext } from '$lib/auth/context';
	import { listSessions, revokeAllSessions, revokeSession, type UserSession } from '$lib/sessions-api';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const auth = authContext.get();

	let sessions = $state<UserSession[]>([]);
	let error = $state<string | null>(null);
	let busy = $state(false);

	async function refresh() {
		try {
			sessions = await listSessions();
		} catch (err) {
			error = String(err);
		}
	}

	async function run(action: () => Promise<void>) {
		busy = true;
		error = null;
		try {
			await action();
			await refresh();
		} catch (err) {
			error = String(err);
		} finally {
			busy = false;
		}
	}

	async function signOutEverywhere() {
		busy = true;
		error = null;
		try {
			await revokeAllSessions(false);
			// The route guard sends the user to /login once currentUser is null.
			await auth.logout();
		} catch (err) {
			error = String(err);
			busy = false;
		}
	}

	$effect(() => {
		void refresh();
	});
</script>

<Card.Root>
	<Card.Header>
		<Card.Title>{m.settingsSessions_heading()}</Card.Title>
		<Card.Description>{m.settingsSessions_description()}</Card.Description>
	</Card.Header>
	<Card.Content class="flex flex-col gap-3">
		{#if error}
			<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
		{/if}
		{#if sessions.length === 0}
			<p class="text-muted-foreground text-sm">{m.settingsSessions_none()}</p>
		{/if}
		{#each sessions as session (session.id)}
			<div class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
				<div class="flex flex-col gap-0.5">
					<span class="flex items-center gap-2 font-medium">
						{m.settingsSessions_signedInAt({ time: formatDateTime(session.createdAt) })}
						{#if session.isCurrent}<Badge variant="secondary">{m.settingsSessions_current()}</Badge>{/if}
					</span>
					<span class="text-muted-foreground text-xs">
						{m.settingsSessions_lastActive({ time: formatDateTime(session.lastSeenAt) })} · {m.settingsSessions_expires({ time: formatDateTime(session.expiresAt) })}
					</span>
				</div>
				{#if !session.isCurrent}
					<Button variant="outline" size="sm" disabled={busy} onclick={() => run(() => revokeSession(session.id))}>
						{m.settingsSessions_revoke()}
					</Button>
				{/if}
			</div>
		{/each}
		<div class="flex flex-wrap gap-2">
			<Button variant="outline" size="sm" disabled={busy || sessions.every((s) => s.isCurrent)} onclick={() => run(() => revokeAllSessions(true))}>
				{m.settingsSessions_revokeOthers()}
			</Button>
			<Button variant="destructive" size="sm" disabled={busy} onclick={signOutEverywhere}>
				{m.settingsSessions_signOutEverywhere()}
			</Button>
		</div>
	</Card.Content>
</Card.Root>
