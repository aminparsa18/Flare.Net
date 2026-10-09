<script lang="ts">
	// Create/edit a managed OTLP forwarding target (ADR-0157). Mounted only while open, so the form state
	// initialises once from `target` instead of being re-synced by an effect. Header values arrive masked;
	// leaving a masked value untouched keeps the stored one (NotificationSecrets server-side).
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Switch } from '$lib/components/ui/switch';
	import { Textarea } from '$lib/components/ui/textarea';
	import type { IngestApiKeyDto } from '$lib/ingest-keys-api';
	import {
		TELEMETRY_SIGNALS,
		saveForwardingTarget,
		type ForwardingTarget,
		type ForwardingTargetRequest,
		type TelemetrySignal
	} from '$lib/telemetry-export-api';
	import { formatHeaderLines, parseHeaderLines, parseList } from '$lib/telemetry-export/format';
	import * as m from '$lib/paraglide/messages';

	let {
		target,
		keys,
		onsaved,
		onclose
	}: {
		/** The target being edited, or null to create one. */
		target: ForwardingTarget | null;
		keys: IngestApiKeyDto[];
		onsaved: () => void;
		onclose: () => void;
	} = $props();

	/* svelte-ignore state_referenced_locally */
	let name = $state(target?.name ?? '');
	/* svelte-ignore state_referenced_locally */
	let endpoint = $state(target?.endpoint ?? '');
	/* svelte-ignore state_referenced_locally */
	let headersText = $state(formatHeaderLines(target?.headers ?? {}));
	/* svelte-ignore state_referenced_locally */
	let signals = $state<TelemetrySignal[]>(target?.signals ?? []);
	/* svelte-ignore state_referenced_locally */
	let servicesText = $state((target?.services ?? []).join(', '));
	/* svelte-ignore state_referenced_locally */
	let ingestKeyIds = $state<string[]>(target?.ingestKeyIds ?? []);
	/* svelte-ignore state_referenced_locally */
	let gzip = $state(target?.gzip ?? true);
	/* svelte-ignore state_referenced_locally */
	let enabled = $state(target?.enabled ?? true);
	let saving = $state(false);
	let error = $state<string | null>(null);

	const parsedHeaders = $derived(parseHeaderLines(headersText));
	const endpointValid = $derived(/^https?:\/\/[^\s/]+/i.test(endpoint.trim()));
	const canSave = $derived(name.trim().length > 0 && endpointValid && parsedHeaders.invalidLine === null);

	// Keys the target already references stay listed even after they are revoked, so saving does not drop them silently.
	const selectableKeys = $derived(keys.filter((k) => k.isActive || ingestKeyIds.includes(k.id)));

	function toggleSignal(signal: TelemetrySignal, on: boolean) {
		signals = on ? [...signals, signal] : signals.filter((s) => s !== signal);
	}

	function toggleKey(id: string, on: boolean) {
		ingestKeyIds = on ? [...ingestKeyIds, id] : ingestKeyIds.filter((k) => k !== id);
	}

	async function save() {
		const request: ForwardingTargetRequest = {
			name: name.trim(),
			enabled,
			endpoint: endpoint.trim(),
			headers: parsedHeaders.headers,
			signals,
			services: parseList(servicesText),
			ingestKeyIds,
			gzip
		};
		saving = true;
		error = null;
		try {
			await saveForwardingTarget(target?.id ?? null, request);
			onsaved();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root open onOpenChange={(next) => !next && onclose()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{target ? m.telemetryExport_fwdDialogEditTitle() : m.telemetryExport_fwdDialogNewTitle()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.telemetryExport_fwdNameLabel()}</span>
				<Input bind:value={name} placeholder={m.telemetryExport_fwdNamePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.telemetryExport_fwdEndpointLabel()}</span>
				<Input bind:value={endpoint} placeholder="https://collector.example.com:4318" class="font-mono" aria-invalid={endpoint !== '' && !endpointValid} />
				<span class="text-muted-foreground text-xs">{m.telemetryExport_fwdEndpointHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.telemetryExport_fwdHeadersLabel()}</span>
				<Textarea bind:value={headersText} rows={3} class="font-mono text-xs" placeholder="Authorization: Bearer ..." aria-invalid={parsedHeaders.invalidLine !== null} />
				<span class="text-xs {parsedHeaders.invalidLine === null ? 'text-muted-foreground' : 'text-destructive'}">
					{parsedHeaders.invalidLine === null ? m.telemetryExport_fwdHeadersHint() : m.telemetryExport_fwdHeadersError({ line: parsedHeaders.invalidLine })}
				</span>
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

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.telemetryExport_fwdServicesLabel()}</span>
				<Input bind:value={servicesText} placeholder="checkout, payments" class="font-mono" />
				<span class="text-muted-foreground text-xs">{m.telemetryExport_fwdServicesHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.telemetryExport_fwdKeysLabel()}</span>
				{#if selectableKeys.length === 0}
					<span class="text-muted-foreground text-xs">{m.telemetryExport_fwdKeysNone()}</span>
				{:else}
					<div class="flex max-h-32 flex-col gap-1 overflow-y-auto">
						{#each selectableKeys as key (key.id)}
							<label class="flex items-center gap-2 text-sm">
								<Checkbox checked={ingestKeyIds.includes(key.id)} onCheckedChange={(v) => toggleKey(key.id, v === true)} />
								{key.name}
								{#if !key.isActive}<span class="text-muted-foreground text-xs">({m.telemetryExport_fwdKeyRevoked()})</span>{/if}
							</label>
						{/each}
					</div>
				{/if}
				<span class="text-muted-foreground text-xs">{m.telemetryExport_fwdKeysHint()}</span>
			</div>

			<label class="flex items-center gap-2 text-sm">
				<Switch size="sm" bind:checked={gzip} />
				{m.telemetryExport_fwdGzipLabel()}
			</label>
			<label class="flex items-center gap-2 text-sm">
				<Switch size="sm" bind:checked={enabled} />
				{m.telemetryExport_enabledLabel()}
			</label>

			{#if error}
				<p class="text-destructive text-xs">{error}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={onclose}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={save} disabled={!canSave || saving}>
				{#if saving}<Spinner class="size-3.5" />{/if}
				{target ? m.alertRuleForm_saveChanges() : m.telemetryExport_fwdCreate()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
