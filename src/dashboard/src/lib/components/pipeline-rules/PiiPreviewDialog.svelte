<script lang="ts">
	// Dry-run of Ingest's managed PII redaction (ADR-0199): scans recent logs or spans and shows what it would mask.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { previewPiiRedaction, previewSpanPiiRedaction, scanForPii, type PiiScanResult, type PiiSpanPreviewResult, type PipelineRulePreviewResult, type PipelineRulePreviewMatch } from '$lib/pipeline-rules-api';
	import * as m from '$lib/paraglide/messages';

	let { open = $bindable(false) }: { open?: boolean } = $props();

	let loading = $state(false);
	let signal = $state<'logs' | 'spans'>('logs');
	let result = $state<PipelineRulePreviewResult | null>(null);
	let spanResult = $state<PiiSpanPreviewResult | null>(null);
	let error = $state<string | null>(null);

	let scanning = $state(false);
	let scan = $state<PiiScanResult | null>(null);

	async function runScan(): Promise<void> {
		scanning = true;
		error = null;
		try {
			scan = await scanForPii(signal);
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			scanning = false;
		}
	}

	async function run(): Promise<void> {
		loading = true;
		error = null;
		try {
			if (signal === 'spans') {
				result = null;
				spanResult = await previewSpanPiiRedaction();
			} else {
				spanResult = null;
				result = await previewPiiRedaction();
			}
		} catch (err) {
			result = null;
			spanResult = null;
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		scan = null;
		if (open) void run();
		else {
			result = null;
			spanResult = null;
		}
	});

	function changedAttributes(match: PipelineRulePreviewMatch): [string, string][] {
		return Object.entries(match.afterAttributes).filter(([k, v]) => match.beforeAttributes[k] !== v);
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{m.piiPreview_title()}</Dialog.Title>
			<Dialog.Description>{m.piiPreview_description()}</Dialog.Description>
		</Dialog.Header>
		<div class="flex flex-col gap-3">
			<div class="flex gap-2">
				<Button size="sm" variant={signal === 'logs' ? 'default' : 'outline'} onclick={() => (signal = 'logs')}>{m.piiSignal_logs()}</Button>
				<Button size="sm" variant={signal === 'spans' ? 'default' : 'outline'} onclick={() => (signal = 'spans')}>{m.piiSignal_spans()}</Button>
			</div>
			{#if loading}
				<Spinner />
			{:else if error}
				<p class="text-destructive text-xs">{error}</p>
			{:else if spanResult}
				<Badge variant={spanResult.changedCount > 0 ? 'warning' : 'outline'} class="self-start">
					{m.pipelineRuleForm_previewSummary({ changed: spanResult.changedCount, sampled: spanResult.sampledCount })}
				</Badge>
				<div class="flex max-h-96 flex-col gap-2 overflow-y-auto">
					{#each spanResult.matches as match (match.traceId + match.spanId)}
						<div class="bg-warning/10 flex flex-col gap-0.5 rounded-md p-1.5 font-mono text-xs">
							<span class="text-muted-foreground font-sans">{match.serviceName} · {match.name}</span>
							{#each match.changes as change (change.field)}
								<span class="text-muted-foreground">{change.field}: <span class="text-foreground">{change.before}</span> → <span class="text-warning">{change.after}</span></span>
							{/each}
						</div>
					{/each}
				</div>
			{:else if result}
				<Badge variant={result.changedCount > 0 ? 'warning' : 'outline'} class="self-start">
					{m.pipelineRuleForm_previewSummary({ changed: result.changedCount, sampled: result.sampledCount })}
				</Badge>
				<div class="flex max-h-96 flex-col gap-2 overflow-y-auto">
					{#each result.matches as match (match.eventId)}
						<div class="bg-warning/10 flex flex-col gap-0.5 rounded-md p-1.5 font-mono text-xs">
							<span class="text-muted-foreground font-sans">{match.serviceName}</span>
							{#if match.afterBody !== match.beforeBody}
								<span class="text-muted-foreground">{m.pipelineRuleForm_previewBeforeLabel()}: <span class="text-foreground">{match.beforeBody}</span></span>
								<span class="text-muted-foreground">{m.pipelineRuleForm_previewAfterLabel()}: <span class="text-warning">{match.afterBody}</span></span>
							{/if}
							{#each changedAttributes(match) as [key, value] (key)}
								<span class="text-muted-foreground">{key}: <span class="text-warning">{value}</span></span>
							{/each}
						</div>
					{/each}
				</div>
			{/if}
			{#if !loading && !error && (result || spanResult)}
				<div class="flex items-center gap-2 border-t pt-3">
					<Button variant="outline" size="sm" onclick={runScan} disabled={scanning}>
						{#if scanning}<Spinner class="size-3.5" />{/if}
						{m.piiScan_button()}
					</Button>
					{#if scan}
						<span class="text-muted-foreground text-xs">{(signal === 'spans' ? m.piiScan_summarySpans : m.piiScan_summary)({ fields: scan.findings.length, sampled: scan.sampledCount })}</span>
					{/if}
				</div>
				{#if scan && scan.findings.length > 0}
					<div class="flex max-h-64 flex-col gap-1 overflow-y-auto">
						{#each scan.findings as f (f.field + f.detector)}
							<div class="flex flex-col gap-0.5 rounded-md border p-1.5 text-xs">
								<div class="flex items-center gap-2">
									<span class="font-mono">{f.field}</span>
									<Badge variant="outline">{f.detector}</Badge>
									<span class="text-muted-foreground">{f.count}</span>
								</div>
								<span class="text-muted-foreground truncate font-mono">{f.example}</span>
							</div>
						{/each}
					</div>
				{/if}
			{/if}
		</div>
	</Dialog.Content>
</Dialog.Root>
