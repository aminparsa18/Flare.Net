<script lang="ts">
	// Create/edit a shared alert notification template (ADR-0148). Same Dialog + reset-on-open
	// $effect shape as MaintenanceWindowFormDialog.svelte. Placeholder validation is the
	// server's (AlertTemplateRequest.Validate) and comes back as the save error.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Switch } from '$lib/components/ui/switch';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { NOTIFICATION_TEMPLATE_PLACEHOLDERS } from '$lib/alerts-api';
	import { TEMPLATE_CHANNEL_TYPES, type AlertTemplate, type AlertTemplateRequest } from '$lib/alert-templates-api';
	import * as m from '$lib/paraglide/messages';

	let {
		target,
		saving,
		saveError,
		onSave,
		onClose
	}: {
		/** `null` closed, `'new'` creating, a template editing it. */
		target: AlertTemplate | 'new' | null;
		saving: boolean;
		saveError: string | null;
		onSave: (request: AlertTemplateRequest) => void;
		onClose: () => void;
	} = $props();

	const open = $derived(target !== null);
	const isEdit = $derived(target !== null && target !== 'new');

	let name = $state('');
	let description = $state('');
	let isDefault = $state(false);
	let titleTemplate = $state('');
	let bodyTemplate = $state('');
	let resolvedBodyTemplate = $state('');
	let channelBodies = $state<Record<string, string>>({});

	$effect(() => {
		if (target === 'new') {
			name = '';
			description = '';
			isDefault = false;
			titleTemplate = '';
			bodyTemplate = '';
			resolvedBodyTemplate = '';
			channelBodies = {};
		} else if (target) {
			name = target.name;
			description = target.description;
			isDefault = target.isDefault;
			titleTemplate = target.titleTemplate;
			bodyTemplate = target.bodyTemplate;
			resolvedBodyTemplate = target.resolvedBodyTemplate;
			channelBodies = { ...target.channelBodies };
		}
	});

	const hasText = $derived(
		titleTemplate.trim() !== '' ||
			bodyTemplate.trim() !== '' ||
			resolvedBodyTemplate.trim() !== '' ||
			Object.values(channelBodies).some((body) => body.trim() !== '')
	);
	const canSave = $derived(name.trim().length > 0 && hasText);

	function buildRequest(): AlertTemplateRequest {
		return {
			name: name.trim(),
			description: description.trim(),
			isDefault,
			titleTemplate: titleTemplate.trim(),
			bodyTemplate: bodyTemplate.trim(),
			resolvedBodyTemplate: resolvedBodyTemplate.trim(),
			channelBodies: Object.fromEntries(Object.entries(channelBodies).map(([k, v]) => [k, v.trim()]).filter(([, v]) => v !== ''))
		};
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && onClose()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.alertTemplateForm_titleEdit() : m.alertTemplateForm_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.alertTemplateForm_namePlaceholder()} maxlength={128} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<div class="flex items-center gap-2">
					<Switch bind:checked={isDefault} />
					<span class="text-xs font-medium">{m.alertTemplateForm_defaultLabel()}</span>
				</div>
				<span class="text-muted-foreground text-xs">{m.alertTemplateForm_defaultHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_templateTitleLabel()}</span>
				<Input bind:value={titleTemplate} placeholder={'[{{status}}] {{rule_name}}'} maxlength={256} />
				<span class="text-muted-foreground text-xs">{m.alertRuleForm_templateTitleHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertTemplateForm_bodyLabel()}</span>
				<Textarea bind:value={bodyTemplate} rows={4} maxlength={2000} class="font-mono text-xs" />
				<span class="text-muted-foreground text-xs">{m.alertRuleForm_templateBodyHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertTemplateForm_resolvedLabel()}</span>
				<Textarea bind:value={resolvedBodyTemplate} rows={3} maxlength={2000} class="font-mono text-xs" />
				<span class="text-muted-foreground text-xs">{m.alertTemplateForm_resolvedHint()}</span>
			</div>

			<details class="text-xs" open={Object.keys(channelBodies).length > 0}>
				<summary class="cursor-pointer font-medium">{m.alertTemplateForm_channelBodiesLabel()}</summary>
				<p class="text-muted-foreground my-1">{m.alertTemplateForm_channelBodiesHint()}</p>
				<div class="flex flex-col gap-2">
					{#each TEMPLATE_CHANNEL_TYPES as type (type)}
						<div class="flex flex-col gap-1">
							<span class="text-muted-foreground">{type}</span>
							<Textarea
								value={channelBodies[type] ?? ''}
								oninput={(e) => (channelBodies = { ...channelBodies, [type]: e.currentTarget.value })}
								rows={2}
								maxlength={2000}
								class="font-mono text-xs"
							/>
						</div>
					{/each}
				</div>
			</details>

			<p class="text-muted-foreground text-xs">
				{m.alertRuleForm_templatePlaceholdersLabel()}
				{#each NOTIFICATION_TEMPLATE_PLACEHOLDERS as placeholder (placeholder)}
					<code class="bg-muted mr-1 rounded px-1">{`{{${placeholder}}}`}</code>
				{/each}
				{m.alertRuleForm_templateLabelsHint()}
			</p>

			{#if saveError}
				<p class="text-destructive text-xs">{saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={onClose}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={() => onSave(buildRequest())} disabled={!canSave || saving}>
				{#if saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.alertTemplateForm_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
