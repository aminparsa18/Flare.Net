<script lang="ts">
	// Managed S3 archive settings (ADR-0156/0157). Remounted by its parent whenever the saved settings change,
	// so the fields initialise once from `settings`. Keys arrive masked; leaving them untouched keeps the stored ones.
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Input } from '$lib/components/ui/input';
	import * as Select from '$lib/components/ui/select';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Switch } from '$lib/components/ui/switch';
	import {
		TELEMETRY_SIGNALS,
		resetArchiveSettings,
		saveArchiveSettings,
		type ArchiveFileFormat,
		type ArchiveSettings,
		type TelemetrySignal
	} from '$lib/telemetry-export-api';
	import * as m from '$lib/paraglide/messages';

	let { settings, onchanged }: { settings: ArchiveSettings; onchanged: (notice: string) => void } = $props();

	const FORMATS: ArchiveFileFormat[] = ['Parquet', 'Ndjson'];

	/* svelte-ignore state_referenced_locally */
	let enabled = $state(settings.enabled);
	/* svelte-ignore state_referenced_locally */
	let endpoint = $state(settings.endpoint);
	/* svelte-ignore state_referenced_locally */
	let accessKey = $state(settings.accessKey);
	/* svelte-ignore state_referenced_locally */
	let secretKey = $state(settings.secretKey);
	/* svelte-ignore state_referenced_locally */
	let prefix = $state(settings.prefix);
	/* svelte-ignore state_referenced_locally */
	let format = $state<ArchiveFileFormat>(settings.format);
	/* svelte-ignore state_referenced_locally */
	let signals = $state<TelemetrySignal[]>(settings.signals);
	let busy = $state<'save' | 'reset' | null>(null);
	let error = $state<string | null>(null);

	const endpointValid = $derived(/^https?:\/\/[^\s/]+/i.test(endpoint.trim()));
	// The API always wants a valid bucket URL; the keys only matter once the archive is enabled.
	const canSave = $derived(endpointValid && (!enabled || (accessKey.trim().length > 0 && secretKey.trim().length > 0)));

	function toggleSignal(signal: TelemetrySignal, on: boolean) {
		signals = on ? [...signals, signal] : signals.filter((s) => s !== signal);
	}

	async function run(kind: 'save' | 'reset', action: () => Promise<unknown>, done: string) {
		busy = kind;
		error = null;
		try {
			await action();
			onchanged(done);
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = null;
		}
	}

	const save = () =>
		run(
			'save',
			() =>
				saveArchiveSettings({
					enabled,
					endpoint: endpoint.trim(),
					accessKey: accessKey.trim(),
					secretKey: secretKey.trim(),
					prefix: prefix.trim(),
					format,
					signals
				}),
			m.telemetryExport_archiveSaved()
		);

	function reset() {
		if (!confirm(m.telemetryExport_archiveResetConfirm())) return;
		return run('reset', resetArchiveSettings, m.telemetryExport_archiveResetDone());
	}
</script>

<div class="flex max-w-xl flex-col gap-3">
	<label class="flex items-center gap-2 text-sm">
		<Switch size="sm" bind:checked={enabled} />
		{m.telemetryExport_archiveEnabled()}
	</label>

	<div class="flex flex-col gap-1">
		<span class="text-xs font-medium">{m.telemetryExport_archiveEndpointLabel()}</span>
		<Input bind:value={endpoint} placeholder="http://rustfs:9000/flare-archive" class="font-mono" aria-invalid={endpoint !== '' && !endpointValid} />
		<span class="text-muted-foreground text-xs">{m.telemetryExport_archiveEndpointHint()}</span>
	</div>

	<div class="flex gap-3">
		<div class="flex flex-1 flex-col gap-1">
			<span class="text-xs font-medium">{m.telemetryExport_archiveAccessKey()}</span>
			<Input bind:value={accessKey} autocomplete="off" class="font-mono" />
		</div>
		<div class="flex flex-1 flex-col gap-1">
			<span class="text-xs font-medium">{m.telemetryExport_archiveSecretKey()}</span>
			<Input bind:value={secretKey} type="password" autocomplete="new-password" class="font-mono" />
		</div>
	</div>
	<span class="text-muted-foreground -mt-2 text-xs">{m.telemetryExport_archiveKeysHint()}</span>

	<div class="flex gap-3">
		<div class="flex flex-1 flex-col gap-1">
			<span class="text-xs font-medium">{m.telemetryExport_archivePrefix()}</span>
			<Input bind:value={prefix} class="font-mono" />
		</div>
		<div class="flex flex-col gap-1">
			<span class="text-xs font-medium">{m.telemetryExport_archiveFormat()}</span>
			<Select.Root type="single" value={format} onValueChange={(v) => (format = v as ArchiveFileFormat)}>
				<Select.Trigger class="w-32">{format}</Select.Trigger>
				<Select.Content>
					{#each FORMATS as f (f)}
						<Select.Item value={f} label={f} />
					{/each}
				</Select.Content>
			</Select.Root>
		</div>
	</div>

	<div class="flex flex-col gap-1">
		<span class="text-xs font-medium">{m.telemetryExport_signalsLabel()}</span>
		<div class="flex gap-4">
			{#each TELEMETRY_SIGNALS as signal (signal)}
				<label class="flex items-center gap-2 text-sm">
					<Checkbox checked={signals.includes(signal)} onCheckedChange={(v) => toggleSignal(signal, v === true)} />
					{signal}
				</label>
			{/each}
		</div>
		<span class="text-muted-foreground text-xs">{m.telemetryExport_signalsHint()}</span>
	</div>

	{#if error}
		<p class="text-destructive text-xs">{error}</p>
	{/if}

	<div class="flex gap-2">
		<Button size="sm" onclick={save} disabled={!canSave || busy !== null}>
			{#if busy === 'save'}<Spinner class="size-3.5" />{/if}
			{m.telemetryExport_archiveSave()}
		</Button>
		{#if settings.saved}
			<Button variant="outline" size="sm" onclick={reset} disabled={busy !== null}>
				{#if busy === 'reset'}<Spinner class="size-3.5" />{/if}
				{m.telemetryExport_archiveReset()}
			</Button>
		{/if}
	</div>
</div>
