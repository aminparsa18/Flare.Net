<script lang="ts">
	// Downloads the user's own saved views and dashboards as one JSON file. Saved views are
	// shared workspace-wide and carry no owner, so every view the user can see is included;
	// dashboards are limited to those the user owns (ADR-0027).
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { authContext } from '$lib/auth/context';
	import { listDashboards } from '$lib/dashboards-api';
	import { listSavedViews } from '$lib/saved-views-api';
	import { downloadBlob } from '$lib/logs/export';
	import * as m from '$lib/paraglide/messages';

	const auth = authContext.get();

	let busy = $state(false);
	let error = $state<string | null>(null);

	async function exportData() {
		busy = true;
		error = null;
		try {
			const [views, dashboards] = await Promise.all([listSavedViews(), listDashboards()]);
			const me = auth.currentUser?.id;
			const payload = {
				exportedAt: new Date().toISOString(),
				user: auth.currentUser?.username ?? null,
				savedViews: views.views,
				dashboards: dashboards.dashboards.filter((d) => d.ownerUserId === me)
			};
			downloadBlob(new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' }), 'flare-my-data.json');
		} catch (err) {
			error = String(err);
		} finally {
			busy = false;
		}
	}
</script>

<Card.Root>
	<Card.Header>
		<Card.Title>{m.settingsExport_heading()}</Card.Title>
		<Card.Description>{m.settingsExport_description()}</Card.Description>
	</Card.Header>
	<Card.Content class="flex flex-col gap-3">
		{#if error}
			<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
		{/if}
		<div>
			<Button variant="outline" size="sm" disabled={busy} onclick={exportData}>{m.settingsExport_button()}</Button>
		</div>
	</Card.Content>
</Card.Root>
