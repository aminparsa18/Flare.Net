<script lang="ts">
	// First real use of the `Table`/`Card` shadcn components in this app (both were
	// scaffolded but unused before this feature - see AlertRuleFormDialog.svelte's own
	// note about `Dialog` being in the same position).
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { alertsContext } from '$lib/alerts/context';
	import { testAlertRule, type AlertRule, type AlertTestResult } from '$lib/alerts-api';
	import { SEVERITY_BUCKETS, severityNumbersForBucket } from '$lib/logs/severity';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import HistoryIcon from '@lucide/svelte/icons/history';
	import PlayIcon from '@lucide/svelte/icons/play';
	import BellIcon from '@lucide/svelte/icons/bell';

	const alerts = alertsContext.get();

	function summarizeCondition(rule: AlertRule): string {
		const parts: string[] = [];
		if (rule.condition.services?.length) parts.push(rule.condition.services.join(', '));
		const severities = rule.condition.severityNumbers ?? [];
		const labels = SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severities.includes(n))).map(
			(b) => b.label
		);
		if (labels.length) parts.push(labels.join('/'));
		if (rule.condition.search) parts.push(`"${rule.condition.search}"`);
		return parts.length ? parts.join(' · ') : 'All logs';
	}

	function thresholdText(rule: AlertRule): string {
		const symbol = rule.threshold.comparator === 'LessThan' ? '<' : '>=';
		return `${symbol} ${rule.threshold.count} in ${rule.windowSeconds}s`;
	}

	async function handleDelete(rule: AlertRule): Promise<void> {
		if (!confirm(`Delete alert "${rule.name}"? This cannot be undone.`)) return;
		await alerts.remove(rule.id);
	}

	// Ephemeral, per-row test results - not part of AlertsState since nothing else in
	// the app needs to react to "did I just test this rule", same reasoning
	// VolumeChart.svelte calls aggregateLogs() directly rather than through
	// LogsExplorerState for its own local concern.
	let testResults = $state<Record<string, AlertTestResult | 'loading' | 'error'>>({});

	async function handleTest(rule: AlertRule): Promise<void> {
		testResults = { ...testResults, [rule.id]: 'loading' };
		try {
			const result = await testAlertRule(rule.id);
			testResults = { ...testResults, [rule.id]: result };
		} catch {
			testResults = { ...testResults, [rule.id]: 'error' };
		}
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">Alerts</h1>
		<p class="text-muted-foreground text-xs">Threshold/query-based rules that notify a webhook or Slack when breached.</p>
	</div>
	<Button size="sm" onclick={() => alerts.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		New alert
	</Button>
</div>

{#if alerts.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if alerts.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{alerts.error}</p>
	</div>
{:else if alerts.rules.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<BellIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>No alert rules yet</Empty.Title>
			<Empty.Description>Create one to get notified when your logs breach a threshold.</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => alerts.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				New alert
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>Name</Table.Head>
					<Table.Head>Condition</Table.Head>
					<Table.Head>Threshold</Table.Head>
					<Table.Head>Cooldown</Table.Head>
					<Table.Head>Status</Table.Head>
					<Table.Head class="text-right">Actions</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each alerts.rules as rule (rule.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{rule.name}
							{#if rule.description}
								<p class="text-muted-foreground font-normal">{rule.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{summarizeCondition(rule)}</Table.Cell>
						<Table.Cell class="font-mono text-xs">{thresholdText(rule)}</Table.Cell>
						<Table.Cell class="text-muted-foreground">{rule.cooldownSeconds}s</Table.Cell>
						<Table.Cell>
							<Badge variant={rule.enabled ? 'secondary' : 'outline'}>{rule.enabled ? 'Enabled' : 'Disabled'}</Badge>
							{#if testResults[rule.id] === 'loading'}
								<Badge variant="outline" class="ml-1">Testing…</Badge>
							{:else if testResults[rule.id] === 'error'}
								<Badge variant="destructive" class="ml-1">Test failed</Badge>
							{:else if testResults[rule.id]}
								{@const result = testResults[rule.id] as AlertTestResult}
								<Badge variant={result.wouldFire ? 'warning' : 'outline'} class="ml-1">
									{result.observedCount} now{result.wouldFire ? ' · would fire' : ''}
								</Badge>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title="Dry-run this rule against current data" onclick={() => handleTest(rule)}>
								<PlayIcon />
							</Button>
							<Button variant="ghost" size="icon-sm" title="History" onclick={() => alerts.openHistory(rule)}>
								<HistoryIcon />
							</Button>
							<Button variant="ghost" size="icon-sm" title="Edit" onclick={() => alerts.openEdit(rule)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title="Delete"
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
