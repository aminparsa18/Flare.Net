<script lang="ts">
	// Download this browser's display preferences as JSON, or restore them from such a file.
	// Import overwrites matching preferences and reloads so every store re-reads its state.
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { downloadBlob } from '$lib/logs/export';
	import { exportPrefs, importPrefs, parsePrefsBackup } from '$lib/prefs-backup';
	import * as m from '$lib/paraglide/messages';

	let fileInput = $state<HTMLInputElement>();
	let busy = $state(false);
	let error = $state<string | null>(null);

	function exportFile() {
		error = null;
		downloadBlob(new Blob([JSON.stringify(exportPrefs(), null, 2)], { type: 'application/json' }), 'flare-preferences.json');
	}

	async function onFile(e: Event & { currentTarget: HTMLInputElement }) {
		const file = e.currentTarget.files?.[0];
		e.currentTarget.value = ''; // allow re-selecting the same file
		if (!file) return;
		busy = true;
		error = null;
		try {
			await importPrefs(parsePrefsBackup(await file.text()));
			location.reload();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
			busy = false;
		}
	}
</script>

<Card.Root>
	<Card.Header>
		<Card.Title>{m.settingsPrefsBackup_heading()}</Card.Title>
		<Card.Description>{m.settingsPrefsBackup_description()}</Card.Description>
	</Card.Header>
	<Card.Content class="flex flex-col gap-3">
		{#if error}
			<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
		{/if}
		<div class="flex gap-2">
			<Button variant="outline" size="sm" disabled={busy} onclick={exportFile}>{m.settingsPrefsBackup_export()}</Button>
			<Button variant="outline" size="sm" disabled={busy} onclick={() => fileInput?.click()}>{m.settingsPrefsBackup_import()}</Button>
			<input bind:this={fileInput} type="file" accept="application/json,.json" class="hidden" onchange={onFile} />
		</div>
	</Card.Content>
</Card.Root>
