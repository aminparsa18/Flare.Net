<script lang="ts">
	// "Explain this exception" (ADR-0103): one click sends the exception, stack trace and throw-site
	// source (redacted server-side) to the admin-configured LLM. On demand only - never on render -
	// and the answer is rendered as plain text, not markdown/HTML, since it is model output.
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { explainException, type ExplainExceptionRequest, type ExplainExceptionResponse } from '$lib/ai-api';
	import * as m from '$lib/paraglide/messages';

	let { request }: { request: ExplainExceptionRequest } = $props();

	let result = $state<ExplainExceptionResponse | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	async function run(): Promise<void> {
		loading = true;
		error = null;
		try {
			result = await explainException(request);
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}
</script>

<div class="mt-2 text-xs">
	{#if result}
		<div class="bg-muted rounded-md border p-3">
			<p class="whitespace-pre-wrap">{result.explanation}</p>
			<p class="text-muted-foreground mt-2">
				{m.explainException_footer({ model: result.model })}{result.includedSource ? '' : ` ${m.explainException_noSource()}`}
			</p>
		</div>
	{:else}
		<Button variant="outline" size="sm" onclick={run} disabled={loading} title={m.explainException_hint()}>
			{#if loading}<Spinner class="mr-1 size-3" />{/if}
			{m.explainException_button()}
		</Button>
		{#if error}
			<p class="text-destructive mt-1">{error}</p>
		{/if}
	{/if}
</div>
