<script lang="ts">
	// Add/edit one dashboard variable's definition (see
	// docs-internal/adr/0025-dashboard-variables.md) - opened from ManageVariablesDialog.svelte,
	// driven by `viewer.variableFormTarget` the same "'new' | existing target | null" shape
	// DashboardsState.formTarget already establishes for dashboards themselves.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import * as Select from '$lib/components/ui/select';
	import type { DashboardViewerState } from '$lib/dashboards/viewer.svelte';
	import type { DashboardAttributeBag, DashboardVariable, DashboardVariableSourceKind, DashboardVariableTarget } from '$lib/dashboards-api';
	import * as m from '$lib/paraglide/messages';

	let { viewer }: { viewer: DashboardViewerState } = $props();

	const open = $derived(viewer.variableFormTarget !== null);
	const isEdit = $derived(viewer.variableFormTarget !== null && viewer.variableFormTarget !== 'new');

	let name = $state('');
	let target = $state<DashboardVariableTarget>('Service');
	let attributeBag = $state<DashboardAttributeBag>('Log');
	let attributeKey = $state('');
	let sourceKind = $state<DashboardVariableSourceKind>('Query');
	/** Comma-separated draft for a `Custom` variable's value list - parsed into `customValues` on submit, same "freeform text, split on submit" shape a URL param list might use elsewhere in this app. */
	let customValuesDraft = $state('');
	let defaultValue = $state('');

	// Resets/seeds the draft whenever the dialog opens for a different target - same "only
	// reacts to identity change, not every keystroke" reasoning DashboardFormDialog's own
	// effect documents.
	$effect(() => {
		const t = viewer.variableFormTarget;
		if (t === 'new') {
			name = '';
			target = 'Service';
			attributeBag = 'Log';
			attributeKey = '';
			sourceKind = 'Query';
			customValuesDraft = '';
			defaultValue = '';
		} else if (t) {
			name = t.name;
			target = t.target;
			attributeBag = t.attributeBag ?? 'Log';
			attributeKey = t.attributeKey ?? '';
			sourceKind = t.sourceKind;
			customValuesDraft = (t.customValues ?? []).join(', ');
			defaultValue = t.defaultValue ?? '';
		}
	});

	const BAG_OPTIONS: { value: DashboardAttributeBag; label: string }[] = [
		{ value: 'Log', label: m.variableForm_bagLog() },
		{ value: 'Span', label: m.variableForm_bagSpan() },
		{ value: 'Resource', label: m.variableForm_bagResource() },
		{ value: 'Scope', label: m.variableForm_bagScope() }
	];

	const canSubmit = $derived(name.trim() !== '' && (target !== 'Attribute' || attributeKey.trim() !== ''));

	function handleOpenChange(next: boolean): void {
		if (!next) viewer.closeVariableForm();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		const current = viewer.variableFormTarget;
		if (!current) return;
		const variable: DashboardVariable = {
			id: current === 'new' ? crypto.randomUUID() : current.id,
			name: name.trim(),
			target,
			attributeBag: target === 'Attribute' ? attributeBag : undefined,
			attributeKey: target === 'Attribute' ? attributeKey.trim() : undefined,
			sourceKind,
			customValues:
				sourceKind === 'Custom'
					? customValuesDraft
							.split(',')
							.map((v) => v.trim())
							.filter(Boolean)
					: undefined,
			defaultValue: defaultValue.trim() || null
		};
		await viewer.saveVariable(variable);
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.variableForm_editTitle() : m.variableForm_createTitle()}</Dialog.Title>
			<Dialog.Description>{m.variableForm_description()}</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-2">
				<label for="variable-form-name" class="text-sm font-medium">{m.variableForm_nameLabel()}</label>
				<Input id="variable-form-name" bind:value={name} required />
			</div>

			<div class="space-y-2">
				<span class="text-sm font-medium">{m.variableForm_targetLabel()}</span>
				<div class="flex gap-2">
					<Button type="button" variant={target === 'Service' ? 'default' : 'outline'} size="sm" onclick={() => (target = 'Service')}>
						{m.variableForm_targetService()}
					</Button>
					<Button type="button" variant={target === 'Attribute' ? 'default' : 'outline'} size="sm" onclick={() => (target = 'Attribute')}>
						{m.variableForm_targetAttribute()}
					</Button>
				</div>
				<p class="text-muted-foreground text-xs">
					{target === 'Service' ? m.variableForm_targetServiceHint() : m.variableForm_targetAttributeHint()}
				</p>
			</div>

			{#if target === 'Attribute'}
				<div class="flex gap-2">
					<div class="flex-1 space-y-2">
						<span class="text-sm font-medium">{m.variableForm_bagLabel()}</span>
						<Select.Root type="single" value={attributeBag} onValueChange={(v) => v && (attributeBag = v as DashboardAttributeBag)}>
							<Select.Trigger class="w-full">
								{BAG_OPTIONS.find((o) => o.value === attributeBag)?.label}
							</Select.Trigger>
							<Select.Content>
								{#each BAG_OPTIONS as option (option.value)}
									<Select.Item value={option.value} label={option.label} />
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
					<div class="flex-1 space-y-2">
						<label for="variable-form-key" class="text-sm font-medium">{m.variableForm_keyLabel()}</label>
						<Input id="variable-form-key" bind:value={attributeKey} placeholder={m.variableForm_keyPlaceholder()} required />
					</div>
				</div>
			{/if}

			<div class="space-y-2">
				<span class="text-sm font-medium">{m.variableForm_sourceLabel()}</span>
				<div class="flex gap-2">
					<Button type="button" variant={sourceKind === 'Query' ? 'default' : 'outline'} size="sm" onclick={() => (sourceKind = 'Query')}>
						{m.variableForm_sourceQuery()}
					</Button>
					<Button type="button" variant={sourceKind === 'Custom' ? 'default' : 'outline'} size="sm" onclick={() => (sourceKind = 'Custom')}>
						{m.variableForm_sourceCustom()}
					</Button>
				</div>
				<p class="text-muted-foreground text-xs">
					{sourceKind === 'Query' ? m.variableForm_sourceQueryHint() : m.variableForm_sourceCustomHint()}
				</p>
			</div>

			{#if sourceKind === 'Custom'}
				<div class="space-y-2">
					<label for="variable-form-custom-values" class="text-sm font-medium">{m.variableForm_customValuesLabel()}</label>
					<Textarea id="variable-form-custom-values" bind:value={customValuesDraft} rows={2} placeholder={m.variableForm_customValuesPlaceholder()} />
				</div>
			{/if}

			<div class="space-y-2">
				<label for="variable-form-default" class="text-sm font-medium">{m.variableForm_defaultValueLabel()}</label>
				<Input id="variable-form-default" bind:value={defaultValue} placeholder={m.variableForm_defaultValuePlaceholder()} />
			</div>

			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => viewer.closeVariableForm()}>{m.variableForm_cancel()}</Button>
				<Button type="submit" disabled={!canSubmit}>{isEdit ? m.variableForm_save() : m.variableForm_add()}</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
