<script lang="ts">
	// Edits a `key=value, key=value` label map as one text field (ADR-0084) - shared by the alert
	// rule form (labels) and the maintenance-window form (label matchers). Keeps its own text so a
	// half-typed value isn't reformatted under the cursor; reports the parsed map and validity up.
	import { Input } from '$lib/components/ui/input';
	import { formatLabels, parseLabels, type Labels } from '$lib/alerts/labels';
	import * as m from '$lib/paraglide/messages';

	let {
		value,
		onChange,
		placeholder = m.labelsInput_placeholder()
	}: { value: Labels; onChange: (labels: Labels, valid: boolean) => void; placeholder?: string } = $props();

	let text = $state('');
	// null until the first sync, so the initial value is always adopted.
	let lastEmitted: string | null = null;

	// Re-sync when the parent swaps the whole value (opening the form for another rule).
	$effect(() => {
		const external = formatLabels(value);
		if (external !== lastEmitted) {
			text = external;
			lastEmitted = external;
		}
	});

	const parsed = $derived(parseLabels(text));

	function handleInput(next: string): void {
		text = next;
		const result = parseLabels(next);
		lastEmitted = formatLabels(result.labels);
		onChange(result.labels, !result.error);
	}
</script>

<Input value={text} {placeholder} aria-invalid={parsed.error} oninput={(e) => handleInput(e.currentTarget.value)} />
{#if parsed.error}
	<span class="text-destructive text-xs">{m.labelsInput_error()}</span>
{/if}
