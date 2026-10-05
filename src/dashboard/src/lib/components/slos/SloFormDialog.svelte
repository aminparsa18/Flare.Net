<script lang="ts">
	// Create/edit an SLO. Same Dialog + reset-on-open $effect shape as MaintenanceWindowFormDialog.svelte;
	// the client-side checks mirror SloRequest.Validate so Save can't submit what the API rejects.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import ProjectPicker from '$lib/components/projects/ProjectPicker.svelte';
	import { projects, projectIdForRequest } from '$lib/projects/store.svelte';
	import { createSlo, updateSlo, SLO_LATENCY_LADDER_MS, type Slo, type SloKind } from '$lib/slos-api';
	import * as m from '$lib/paraglide/messages';

	let {
		target,
		serviceNames,
		onclose,
		onsaved
	}: {
		/** The SLO being edited, 'new' to create, or null when closed. */
		target: Slo | 'new' | null;
		/** Known service names, offered as suggestions; any name is accepted. */
		serviceNames: string[];
		onclose: () => void;
		onsaved: (slo: Slo) => void;
	} = $props();

	const open = $derived(target !== null);
	const isEdit = $derived(target !== null && target !== 'new');

	const WINDOW_DAYS = [7, 14, 28, 30, 90];

	let name = $state('');
	let description = $state('');
	let kind = $state<SloKind>('Availability');
	let serviceName = $state('');
	let operationName = $state('');
	let targetPercent = $state('99.5');
	let latencyThresholdMs = $state(500);
	let windowDays = $state(28);
	let projectId = $state<string | null>(null);
	let saving = $state(false);
	let saveError = $state<string | null>(null);

	$effect(() => {
		if (target === 'new') {
			name = '';
			description = '';
			kind = 'Availability';
			serviceName = '';
			operationName = '';
			targetPercent = '99.5';
			latencyThresholdMs = 500;
			windowDays = 28;
			projectId = projects.defaultForNew;
		} else if (target) {
			name = target.name;
			description = target.description;
			kind = target.kind;
			serviceName = target.serviceName;
			operationName = target.operationName;
			targetPercent = String(target.targetPercent);
			latencyThresholdMs = target.latencyThresholdMs || 500;
			windowDays = target.windowDays;
			projectId = target.projectId;
		}
		saveError = null;
	});

	const targetValue = $derived(Number(targetPercent));
	const targetValid = $derived(Number.isFinite(targetValue) && targetValue >= 1 && targetValue <= 99.999);
	const canSave = $derived(name.trim().length > 0 && serviceName.trim().length > 0 && targetValid);

	async function save(): Promise<void> {
		saving = true;
		saveError = null;
		try {
			const request = {
				name: name.trim(),
				description: description.trim(),
				kind,
				serviceName: serviceName.trim(),
				operationName: operationName.trim(),
				targetPercent: targetValue,
				latencyThresholdMs: kind === 'Latency' ? latencyThresholdMs : null,
				windowDays,
				projectId: projectIdForRequest(projectId, isEdit ? (target as Slo).projectId : null)
			};
			onsaved(isEdit ? await updateSlo((target as Slo).id, request) : await createSlo(request));
		} catch (e) {
			saveError = e instanceof Error ? e.message : String(e);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && onclose()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.sloForm_titleEdit() : m.sloForm_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.sloForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.sloForm_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.sloForm_descriptionLabel()}</span>
				<Textarea bind:value={description} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.sloForm_kindLabel()}</span>
				<Select.Root type="single" value={kind} onValueChange={(v) => v && (kind = v as SloKind)}>
					<Select.Trigger class="w-60">
						{kind === 'Latency' ? m.sloKind_latency() : m.sloKind_availability()}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="Availability" label={m.sloKind_availability()} />
						<Select.Item value="Latency" label={m.sloKind_latency()} />
					</Select.Content>
				</Select.Root>
				<span class="text-muted-foreground text-xs">{kind === 'Latency' ? m.sloForm_kindHintLatency() : m.sloForm_kindHintAvailability()}</span>
			</div>

			<ProjectPicker bind:value={projectId} />

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.sloForm_serviceLabel()}</span>
				<Input bind:value={serviceName} list="slo-service-names" />
				<datalist id="slo-service-names">
					{#each serviceNames as service (service)}
						<option value={service}></option>
					{/each}
				</datalist>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.sloForm_operationLabel()}</span>
				<Input bind:value={operationName} placeholder="GET /orders/{'{id}'}" />
				<span class="text-muted-foreground text-xs">{m.sloForm_operationHint()}</span>
			</div>

			{#if kind === 'Latency'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.sloForm_thresholdLabel()}</span>
					<Select.Root type="single" value={String(latencyThresholdMs)} onValueChange={(v) => v && (latencyThresholdMs = Number(v))}>
						<Select.Trigger class="w-40">{latencyThresholdMs} ms</Select.Trigger>
						<Select.Content>
							{#each SLO_LATENCY_LADDER_MS as ms (ms)}
								<Select.Item value={String(ms)} label={`${ms} ms`} />
							{/each}
						</Select.Content>
					</Select.Root>
					<span class="text-muted-foreground text-xs">{m.sloForm_thresholdHint()}</span>
				</div>
			{/if}

			<div class="grid grid-cols-2 gap-2">
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.sloForm_targetLabel()}</span>
					<Input bind:value={targetPercent} inputmode="decimal" aria-invalid={!targetValid} />
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.sloForm_windowLabel()}</span>
					<Select.Root type="single" value={String(windowDays)} onValueChange={(v) => v && (windowDays = Number(v))}>
						<Select.Trigger class="w-full">{m.sloForm_days({ days: windowDays })}</Select.Trigger>
						<Select.Content>
							{#each WINDOW_DAYS as days (days)}
								<Select.Item value={String(days)} label={m.sloForm_days({ days })} />
							{/each}
						</Select.Content>
					</Select.Root>
				</div>
			</div>
			{#if !targetValid}
				<p class="text-destructive text-xs">{m.sloForm_errorTarget()}</p>
			{/if}

			{#if saveError}
				<p class="text-destructive text-xs">{saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={onclose}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={save} disabled={!canSave || saving}>
				{#if saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.sloForm_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
