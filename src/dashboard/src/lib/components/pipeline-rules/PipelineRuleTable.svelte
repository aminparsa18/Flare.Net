<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { pipelineRulesContext } from '$lib/pipeline-rules/context';
	import type { PipelineRule } from '$lib/pipeline-rules-api';
	import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import ShieldIcon from '@lucide/svelte/icons/shield';

	const pipelineRules = pipelineRulesContext.get();

	// Same shape as AlertRuleTable.svelte's summarizeCondition - "matches all logs" is a
	// visible, deliberate state here too (see docs-internal/adr/0033-pipeline-rules-extraction-redaction.md's
	// no-scoping-condition safety note), not just an Alerts-page convention.
	function summarizeCondition(rule: PipelineRule): string {
		const parts: string[] = [];
		if (rule.condition.services?.length) parts.push(rule.condition.services.join(', '));
		const severities = rule.condition.severityNumbers ?? [];
		const labels = SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severities.includes(n))).map((b) =>
			severityBucketLabel(b)
		);
		if (labels.length) parts.push(labels.join('/'));
		if (rule.condition.search) parts.push(`"${rule.condition.search}"`);
		return parts.length ? parts.join(' · ') : m.pipelineRuleTable_allLogs();
	}

	function summarizeActions(rule: PipelineRule): string {
		return rule.actions
			.map((a) => (a.kind === 'ExtractRegex' ? m.pipelineRuleTable_actionExtract() : m.pipelineRuleTable_actionRedact()))
			.join(', ');
	}

	async function handleDelete(rule: PipelineRule): Promise<void> {
		if (!confirm(m.pipelineRuleTable_deleteConfirm({ name: rule.name }))) return;
		await pipelineRules.remove(rule.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.pipelineRuleTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.pipelineRuleTable_subheading()}</p>
	</div>
	<Button size="sm" onclick={() => pipelineRules.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.pipelineRuleTable_newRule()}
	</Button>
</div>

{#if pipelineRules.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if pipelineRules.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{pipelineRules.error}</p>
	</div>
{:else if pipelineRules.rules.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<ShieldIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.pipelineRuleTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.pipelineRuleTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => pipelineRules.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.pipelineRuleTable_newRule()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.pipelineRuleTable_colName()}</Table.Head>
					<Table.Head>{m.pipelineRuleTable_colCondition()}</Table.Head>
					<Table.Head>{m.pipelineRuleTable_colExtractRedact()}</Table.Head>
					<Table.Head>{m.pipelineRuleTable_colStatus()}</Table.Head>
					<Table.Head class="text-right">{m.pipelineRuleTable_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each pipelineRules.rules as rule (rule.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{rule.name}
							{#if rule.description}
								<p class="text-muted-foreground font-normal">{rule.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{summarizeCondition(rule)}</Table.Cell>
						<Table.Cell class="text-muted-foreground">{summarizeActions(rule)}</Table.Cell>
						<Table.Cell>
							<Badge variant={rule.enabled ? 'secondary' : 'outline'}>
								{rule.enabled ? m.pipelineRuleTable_enabled() : m.pipelineRuleTable_disabled()}
							</Badge>
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.pipelineRuleTable_actionEdit()} onclick={() => pipelineRules.openEdit(rule)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.pipelineRuleTable_actionDelete()}
								onclick={() => handleDelete(rule)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}
