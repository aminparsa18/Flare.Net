<script lang="ts">
	// Create/edit pipeline rule. Same Dialog-not-Sheet shape AlertRuleFormDialog.svelte's
	// own comment explains - a bounded form, not a detail viewer.
	import { onMount } from 'svelte';
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Switch } from '$lib/components/ui/switch';
	import { Spinner } from '$lib/components/ui/spinner';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import { pipelineRulesContext } from '$lib/pipeline-rules/context';
	import type { PipelineRuleRequest, PipelineRuleAction, RuleActionKind } from '$lib/pipeline-rules-api';
	import { aggregateLogs } from '$lib/api';
	import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';

	/** A form-editable draft of one `PipelineRuleAction` - flat fields regardless of `kind`, converted to the kind-specific wire shape in `buildRequest()`. `replacement` is only meaningful for `RedactRegex`. */
	interface ActionDraft {
		kind: RuleActionKind;
		sourceAttributeKey: string;
		pattern: string;
		replacement: string;
	}

	function newActionDraft(): ActionDraft {
		return { kind: 'ExtractRegex', sourceAttributeKey: '', pattern: '', replacement: '***' };
	}

	function draftFromAction(action: PipelineRuleAction): ActionDraft {
		if (action.kind === 'RedactRegex') {
			return {
				kind: 'RedactRegex',
				sourceAttributeKey: action.redactRegex?.sourceAttributeKey ?? '',
				pattern: action.redactRegex?.pattern ?? '',
				replacement: action.redactRegex?.replacement ?? '***'
			};
		}
		return {
			kind: 'ExtractRegex',
			sourceAttributeKey: action.extractRegex?.sourceAttributeKey ?? '',
			pattern: action.extractRegex?.pattern ?? '',
			replacement: '***'
		};
	}

	const pipelineRules = pipelineRulesContext.get();

	const open = $derived(pipelineRules.formTarget !== null);
	const isEdit = $derived(pipelineRules.formTarget !== null && pipelineRules.formTarget !== 'new');

	let name = $state('');
	let description = $state('');
	let enabled = $state(true);
	let services = $state<string[]>([]);
	let severityNumbers = $state<number[]>([]);
	let search = $state('');
	let actions = $state<ActionDraft[]>([newActionDraft()]);

	// Resets the draft whenever the dialog opens for a different target - same "only
	// transitions at open/close time" reasoning AlertRuleFormDialog.svelte's own reset
	// $effect documents.
	$effect(() => {
		const target = pipelineRules.formTarget;
		if (target === 'new') {
			name = '';
			description = '';
			enabled = true;
			services = [];
			severityNumbers = [];
			search = '';
			actions = [newActionDraft()];
		} else if (target) {
			name = target.name;
			description = target.description;
			enabled = target.enabled;
			services = target.condition.services ? [...target.condition.services] : [];
			severityNumbers = target.condition.severityNumbers ? [...target.condition.severityNumbers] : [];
			search = target.condition.search ?? '';
			actions = target.actions.length ? target.actions.map(draftFromAction) : [newActionDraft()];
		}
	});

	// One-off wide-window aggregate to enumerate service names for the picker - same
	// approach AlertRuleFormDialog.svelte's loadKnownServices uses, duplicated rather than
	// shared for the same "no Explorer state to borrow one from" reasoning.
	let knownServices = $state<string[]>([]);
	onMount(() => {
		void loadKnownServices();
	});
	async function loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await aggregateLogs({
				filter: { from: from.toISOString(), to: to.toISOString() },
				bucketWidthSeconds: 7 * 24 * 60 * 60,
				groupBy: 'Service'
			});
			knownServices = [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		} catch {
			// Non-critical - the picker just shows fewer/no options until a retry.
		}
	}

	const serviceOptions = $derived(knownServices.map((s) => ({ value: s, label: s })));
	const severityOptions = $derived(SEVERITY_BUCKETS.map((b) => ({ value: b.id, label: severityBucketLabel(b) })));
	const selectedSeverityIds = $derived(
		SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severityNumbers.includes(n))).map((b) => b.id)
	);
	function handleSeverityChange(ids: string[]): void {
		const numbers = ids.flatMap((id) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.id === id);
			return bucket ? severityNumbersForBucket(bucket) : [];
		});
		severityNumbers = [...new Set(numbers)];
	}

	/** Every field this dialog can set on `PipelineRuleCondition` empty - the same emptiness `PipelineRuleConditionMatcher`/`LogFilterMatcher` treat as "matches every log." Surfaced explicitly rather than left as a silent default - see docs-internal/adr/0033-pipeline-rules-extraction-redaction.md's no-scoping-condition safety note. */
	const matchesAllLogs = $derived(services.length === 0 && severityNumbers.length === 0 && search.trim().length === 0);

	function addAction(): void {
		actions = [...actions, newActionDraft()];
	}

	function removeAction(index: number): void {
		actions = actions.filter((_, i) => i !== index);
	}

	const canSave = $derived(
		name.trim().length > 0 && actions.length > 0 && actions.every((a) => a.pattern.trim().length > 0)
	);

	function buildRequest(): PipelineRuleRequest {
		return {
			name: name.trim(),
			description: description.trim(),
			enabled,
			condition: {
				services: services.length ? [...services] : undefined,
				severityNumbers: severityNumbers.length ? [...severityNumbers] : undefined,
				search: search.trim() || undefined
			},
			actions: actions.map(
				(a): PipelineRuleAction =>
					a.kind === 'RedactRegex'
						? {
								kind: 'RedactRegex',
								redactRegex: {
									sourceAttributeKey: a.sourceAttributeKey.trim() || undefined,
									pattern: a.pattern.trim(),
									replacement: a.replacement
								}
							}
						: {
								kind: 'ExtractRegex',
								extractRegex: { sourceAttributeKey: a.sourceAttributeKey.trim() || undefined, pattern: a.pattern.trim() }
							}
			)
		};
	}

	async function handleSave(): Promise<void> {
		const target = pipelineRules.formTarget;
		const request = buildRequest();
		if (target && target !== 'new') {
			await pipelineRules.update(target.id, request);
		} else {
			await pipelineRules.create(request);
		}
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && pipelineRules.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.pipelineRuleForm_titleEdit() : m.pipelineRuleForm_titleNew()}</Dialog.Title>
			<Dialog.Description>{m.pipelineRuleForm_description()}</Dialog.Description>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.pipelineRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.pipelineRuleForm_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.pipelineRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.pipelineRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.pipelineRuleForm_conditionLabel()}</span>
				<div class="flex flex-wrap items-center gap-2">
					<PopoverMultiSelect
						label={m.pipelineRuleForm_serviceLabel()}
						options={serviceOptions}
						selected={services}
						onChange={(next) => (services = next)}
					/>
					<PopoverMultiSelect
						label={m.pipelineRuleForm_levelLabel()}
						options={severityOptions}
						selected={selectedSeverityIds}
						onChange={handleSeverityChange}
					/>
				</div>
				<Input bind:value={search} placeholder={m.pipelineRuleForm_searchPlaceholder()} />
				{#if matchesAllLogs}
					<div class="text-warning bg-warning/10 flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs">
						<TriangleAlertIcon class="size-3.5 shrink-0" />
						{m.pipelineRuleForm_matchesAllLogsWarning()}
					</div>
				{/if}
			</div>

			<div class="flex flex-col gap-2">
				<div class="flex items-center justify-between">
					<span class="text-xs font-medium">{m.pipelineRuleForm_actionsLabel()}</span>
					<Button variant="outline" size="sm" onclick={addAction}>
						<PlusIcon data-icon="inline-start" />
						{m.pipelineRuleForm_addAction()}
					</Button>
				</div>
				{#each actions as action, i (i)}
					<div class="flex flex-col gap-2 rounded-md border p-2">
						<div class="flex items-center gap-2">
							<Select.Root type="single" value={action.kind} onValueChange={(v) => v && (action.kind = v as RuleActionKind)}>
								<Select.Trigger class="w-40">
									{action.kind === 'RedactRegex' ? m.pipelineRuleForm_kindRedact() : m.pipelineRuleForm_kindExtract()}
								</Select.Trigger>
								<Select.Content>
									<Select.Item value="ExtractRegex" label={m.pipelineRuleForm_kindExtract()} />
									<Select.Item value="RedactRegex" label={m.pipelineRuleForm_kindRedact()} />
								</Select.Content>
							</Select.Root>
							<Input bind:value={action.sourceAttributeKey} placeholder={m.pipelineRuleForm_sourcePlaceholder()} class="flex-1" />
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.pipelineRuleForm_removeAction()}
								onclick={() => removeAction(i)}
								disabled={actions.length === 1}
							>
								<Trash2Icon />
							</Button>
						</div>
						<Input
							bind:value={action.pattern}
							placeholder={action.kind === 'ExtractRegex' ? m.pipelineRuleForm_patternExtractPlaceholder() : m.pipelineRuleForm_patternRedactPlaceholder()}
							class="font-mono text-xs"
						/>
						{#if action.kind === 'ExtractRegex'}
							<span class="text-muted-foreground text-xs">{m.pipelineRuleForm_extractHint()}</span>
						{:else}
							<Input bind:value={action.replacement} placeholder={m.pipelineRuleForm_replacementPlaceholder()} class="font-mono text-xs" />
						{/if}
					</div>
				{/each}
			</div>

			<div class="flex items-center gap-2">
				<Switch bind:checked={enabled} />
				<span class="text-xs">{m.pipelineRuleForm_enabledLabel()}</span>
			</div>

			{#if pipelineRules.saveError}
				<p class="text-destructive text-xs">{pipelineRules.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => pipelineRules.closeForm()}>{m.pipelineRuleForm_cancel()}</Button>
			<Button size="sm" onclick={handleSave} disabled={!canSave || pipelineRules.saving}>
				{#if pipelineRules.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.pipelineRuleForm_saveChanges() : m.pipelineRuleForm_createRule()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
