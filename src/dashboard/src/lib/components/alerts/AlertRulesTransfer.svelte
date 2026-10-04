<script lang="ts">
	// Export/Import of alert rules as portable JSON (channels and SLOs by name). Import always
	// starts with a dry run and only creates rules after the user confirms the summary.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { alertsContext } from '$lib/alerts/context';
	import { exportAlertRules, importAlertRules, type AlertRulesImportResult } from '$lib/alerts-api';
	import { downloadBlob } from '$lib/logs/export';
	import * as m from '$lib/paraglide/messages';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import UploadIcon from '@lucide/svelte/icons/upload';

	// Ids to export; empty exports every rule (the unfiltered view).
	let { ruleIds = [] }: { ruleIds?: string[] } = $props();

	const alerts = alertsContext.get();

	let fileInput: HTMLInputElement | undefined = $state();
	let exportError = $state<string | null>(null);
	let pending = $state<unknown>(null);
	let preview = $state<AlertRulesImportResult | null>(null);
	let dialogError = $state<string | null>(null);
	let busy = $state(false);

	async function doExport(): Promise<void> {
		exportError = null;
		try {
			const doc = await exportAlertRules(ruleIds);
			downloadBlob(new Blob([JSON.stringify(doc, null, 2)], { type: 'application/json' }), 'flare-alert-rules.json');
		} catch (err) {
			exportError = err instanceof Error ? err.message : String(err);
		}
	}

	async function onFileChange(event: Event): Promise<void> {
		const input = event.currentTarget as HTMLInputElement;
		const file = input.files?.[0];
		input.value = '';
		if (!file) return;
		dialogError = null;
		preview = null;
		try {
			pending = JSON.parse(await file.text());
		} catch {
			pending = null;
			dialogError = m.alertTransfer_invalidFile();
			return;
		}
		busy = true;
		try {
			preview = await importAlertRules(pending, true);
		} catch (err) {
			dialogError = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}

	async function confirm(): Promise<void> {
		busy = true;
		dialogError = null;
		try {
			await importAlertRules(pending, false);
			close();
			await alerts.load();
		} catch (err) {
			dialogError = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}

	function close(): void {
		pending = null;
		preview = null;
		dialogError = null;
	}

	const open = $derived(pending !== null || dialogError !== null);
	const badge = { Create: 'default', Skip: 'secondary', Error: 'destructive' } as const;
	const outcomeLabel = { Create: () => m.alertTransfer_outcomeCreate(), Skip: () => m.alertTransfer_outcomeSkip(), Error: () => m.alertTransfer_outcomeError() };
</script>

<input bind:this={fileInput} type="file" accept="application/json" class="hidden" onchange={onFileChange} />
<Button size="sm" variant="outline" title={m.alertTransfer_importTitle()} onclick={() => fileInput?.click()}>
	<UploadIcon data-icon="inline-start" />
	{m.alertTransfer_import()}
</Button>
<Button size="sm" variant="outline" title={exportError ?? m.alertTransfer_exportTitle()} onclick={doExport}>
	<DownloadIcon data-icon="inline-start" />
	{m.alertTransfer_export()}
</Button>

<Dialog.Root {open} onOpenChange={(next) => !next && close()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.alertTransfer_dialogTitle()}</Dialog.Title>
			<Dialog.Description>{m.alertTransfer_dialogDescription()}</Dialog.Description>
		</Dialog.Header>
		{#if dialogError}
			<p class="text-destructive text-sm">{dialogError}</p>
		{/if}
		{#if preview}
			<p class="text-muted-foreground text-xs">{m.alertTransfer_summary({ create: preview.created, skip: preview.skipped, error: preview.errors })}</p>
			<ul class="flex flex-col gap-2">
				{#each preview.items as item, i (i)}
					<li class="flex items-start gap-2 text-sm">
						<Badge variant={badge[item.outcome]}>{outcomeLabel[item.outcome]()}</Badge>
						<div class="min-w-0">
							<div class="truncate font-medium">{item.name}</div>
							{#if item.message}<div class="text-muted-foreground text-xs">{item.message}</div>{/if}
						</div>
					</li>
				{/each}
			</ul>
		{/if}
		<Dialog.Footer>
			<Button variant="ghost" onclick={close}>{m.alertTransfer_close()}</Button>
			{#if preview && preview.created > 0}
				<Button disabled={busy} onclick={confirm}>{m.alertTransfer_confirm({ count: preview.created })}</Button>
			{/if}
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
