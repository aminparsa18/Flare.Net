<script lang="ts">
	// Create/edit a managed OTLP forwarding target (ADR-0157). Mounted only while open, so the form state
	// initialises once from `target` instead of being re-synced by an effect. Header values arrive masked;
	// leaving a masked value untouched keeps the stored one (NotificationSecrets server-side). A new target can start
	// from the Grafana Cloud preset, which derives the endpoint and Basic header from region, instance id and token;
	// what is saved is an ordinary target, so editing it later shows the plain endpoint and (masked) headers.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Checkbox } from '#lib/components/ui/checkbox/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { Switch } from '#lib/components/ui/switch/index.ts';
	import { Textarea } from '#lib/components/ui/textarea/index.ts';
	import type { IngestApiKeyDto } from '#lib/ingest-keys-api.ts';
	import {
		TELEMETRY_SIGNALS,
		saveForwardingTarget,
		testForwardingTarget,
		type ForwardingTarget,
		type ForwardingTestResult,
		type ForwardingTargetRequest,
		type TelemetrySignal
	} from '#lib/telemetry-export-api.ts';
	import { formatHeaderLines, parseHeaderLines, parseList } from '#lib/telemetry-export/format.ts';
	import { grafanaCloudEndpoint, grafanaCloudHeaders, isGrafanaInstanceId, normalizeGrafanaZone } from '#lib/telemetry-export/grafana-cloud.ts';
	import * as m from '#lib/paraglide/messages.js';

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
	let preset = $state<'custom' | 'grafana'>('custom');
	let grafanaZone = $state('');
	let grafanaInstance = $state('');
	let grafanaToken = $state('');
	let testing = $state(false);
	let testResult = $state<ForwardingTestResult | null>(null);
	let testError = $state<string | null>(null);

	const grafana = $derived(preset === 'grafana');
	const zone = $derived(normalizeGrafanaZone(grafanaZone));
	const grafanaValid = $derived(zone !== null && isGrafanaInstanceId(grafanaInstance) && grafanaToken.trim().length > 0);
	const parsedHeaders = $derived(parseHeaderLines(headersText));
	// What gets tested and saved: the preset's derived values, or the plain fields.
	const effectiveEndpoint = $derived(grafana ? (zone ? grafanaCloudEndpoint(zone) : '') : endpoint.trim());
	const effectiveHeaders = $derived(grafana ? grafanaCloudHeaders(grafanaInstance, grafanaToken) : parsedHeaders.headers);
	const endpointValid = $derived(/^https?:\/\/[^\s/]+/i.test(effectiveEndpoint));
	const destinationValid = $derived(grafana ? grafanaValid : endpointValid && parsedHeaders.invalidLine === null);
	const canSave = $derived(name.trim().length > 0 && destinationValid);

	// Keys the target already references stay listed even after they are revoked, so saving does not drop them silently.
	const selectableKeys = $derived(keys.filter((k) => k.isActive || ingestKeyIds.includes(k.id)));

	function toggleSignal(signal: TelemetrySignal, on: boolean) {
		signals = on ? [...signals, signal] : signals.filter((s) => s !== signal);
	}

	function toggleKey(id: string, on: boolean) {
		ingestKeyIds = on ? [...ingestKeyIds, id] : ingestKeyIds.filter((k) => k !== id);
	}

	function setPreset(next: 'custom' | 'grafana') {
		preset = next;
		testResult = null;
		testError = null;
	}

	async function runTest() {
		testing = true;
		testResult = null;
		testError = null;
		try {
			testResult = await testForwardingTarget({ id: target?.id ?? null, endpoint: effectiveEndpoint, headers: effectiveHeaders });
		} catch (err) {
			testError = err instanceof Error ? err.message : String(err);
		} finally {
			testing = false;
		}
	}

	async function save() {
		const request: ForwardingTargetRequest = {
			name: name.trim(),
			enabled,
			endpoint: effectiveEndpoint,
			headers: effectiveHeaders,
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

			{#if !target}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.telemetryExport_fwdPresetLabel()}</span>
					<div class="flex gap-2">
						<Button variant={grafana ? 'outline' : 'secondary'} size="sm" onclick={() => setPreset('custom')} aria-pressed={!grafana}>
							{m.telemetryExport_fwdPresetCustom()}
						</Button>
						<Button variant={grafana ? 'secondary' : 'outline'} size="sm" onclick={() => setPreset('grafana')} aria-pressed={grafana}>
							{m.telemetryExport_fwdPresetGrafana()}
						</Button>
					</div>
				</div>
			{/if}

			{#if grafana}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.telemetryExport_fwdGrafanaZoneLabel()}</span>
					<Input bind:value={grafanaZone} placeholder="prod-us-central-0" class="font-mono" aria-invalid={grafanaZone !== '' && zone === null} />
					<span class="text-xs {grafanaZone === '' || zone !== null ? 'text-muted-foreground' : 'text-destructive'}">
						{grafanaZone === '' || zone !== null ? m.telemetryExport_fwdGrafanaZoneHint() : m.telemetryExport_fwdGrafanaZoneError()}
					</span>
					{#if zone}
						<span class="text-muted-foreground font-mono text-xs break-all">{m.telemetryExport_fwdGrafanaSendsTo({ endpoint: grafanaCloudEndpoint(zone) })}</span>
					{/if}
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.telemetryExport_fwdGrafanaInstanceLabel()}</span>
					<Input bind:value={grafanaInstance} placeholder="123456" inputmode="numeric" class="font-mono" aria-invalid={grafanaInstance !== '' && !isGrafanaInstanceId(grafanaInstance)} />
					{#if grafanaInstance !== '' && !isGrafanaInstanceId(grafanaInstance)}
						<span class="text-destructive text-xs">{m.telemetryExport_fwdGrafanaInstanceError()}</span>
					{/if}
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.telemetryExport_fwdGrafanaTokenLabel()}</span>
					<Input bind:value={grafanaToken} type="password" autocomplete="off" placeholder="glc_..." class="font-mono" />
					<span class="text-muted-foreground text-xs">{m.telemetryExport_fwdGrafanaTokenHint()}</span>
				</div>
			{:else}
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
			{/if}

			<div class="flex flex-col gap-1">
				<div class="flex items-center gap-2">
					<Button variant="outline" size="sm" onclick={runTest} disabled={!destinationValid || testing}>
						{#if testing}<Spinner class="size-3.5" />{/if}
						{m.telemetryExport_fwdTest()}
					</Button>
				</div>
				{#if testResult}
					<p class="text-xs {testResult.success ? 'text-muted-foreground' : 'text-destructive'}" role="status">
						{testResult.success ? m.telemetryExport_fwdTestOk({ url: testResult.url, status: testResult.statusCode }) : testResult.error}
					</p>
				{/if}
				{#if testError}
					<p class="text-destructive text-xs" role="status">{testError}</p>
				{/if}
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
