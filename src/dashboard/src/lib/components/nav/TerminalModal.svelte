<script lang="ts">
	// A flare.cli-*styled* command surface over this dashboard's own APIs - not a real
	// shell, no process execution, no host access. Every command in $lib/terminal
	// reaches an already-authenticated dashboard API the same way clicking around the
	// UI would; see $lib/terminal/registry.ts for how new commands slot in.
	//
	// Self-contained trigger + Dialog in one component (same shape as
	// PatternsModal.svelte), not the lifted-state pattern AppNav/CommandPalette use for
	// commandPaletteOpen - nothing else needs to open this modal.
	import { tick, onDestroy } from 'svelte';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import TerminalIcon from '@lucide/svelte/icons/terminal';
	import { resolveCommand, parseCommandLine } from '$lib/terminal/registry';
	import type { TerminalHandle, TerminalLine, TerminalLineKind } from '$lib/terminal/types';

	let open = $state(false);
	let lines = $state<TerminalLine[]>([]);
	let inputValue = $state('');
	let running = $state(false);
	let outputEl: HTMLDivElement | null = null;
	let inputEl: HTMLInputElement | null = $state(null);

	// Shown only while `lines` is empty (before the first command) - a first-run
	// hint, not a command list (that's `help`'s job). Clicking one fills the input
	// rather than submitting immediately, so it's a starting point to edit, not a
	// surprise action.
	const EXAMPLES = ['tail', 'tail -l error', 'help'];

	function useExample(example: string): void {
		inputValue = example;
		inputEl?.focus();
	}

	// Recall buffer for Up/Down arrow, separate from `lines` (which holds rendered
	// output, not just submitted commands).
	let recall = $state<string[]>([]);
	let recallIndex = $state<number | null>(null);

	// Not $state - nothing renders based on the handle itself, only on `running`.
	let activeHandle: TerminalHandle | null = null;

	function write(text: string, kind: TerminalLineKind = 'output'): void {
		lines.push({ kind, text });
		void tick().then(() => outputEl?.scrollTo({ top: outputEl.scrollHeight }));
	}

	function lineClass(kind: TerminalLineKind): string {
		switch (kind) {
			case 'error':
				return 'text-destructive';
			case 'input':
			case 'info':
				return 'text-muted-foreground';
			default:
				return 'text-foreground';
		}
	}

	function stopActive(): void {
		activeHandle?.cancel();
		activeHandle = null;
		running = false;
	}

	function submit(): void {
		const raw = inputValue;
		inputValue = '';
		if (raw.trim().length === 0) return;

		recall.push(raw);
		recallIndex = null;
		write(`flare> ${raw}`, 'input');

		// Only one streaming command runs at a time - starting a new one cancels
		// whatever was already running, same expectation a real terminal sets.
		stopActive();

		const [name, ...args] = parseCommandLine(raw);
		if (name === undefined) return;

		if (name.toLowerCase() === 'clear') {
			lines = [];
			return;
		}

		const command = resolveCommand(name);
		if (!command) {
			write(`${name}: command not found (try 'help')`, 'error');
			return;
		}

		const result = command.run(args, { writeLine: write });
		if (result && typeof (result as TerminalHandle).cancel === 'function') {
			activeHandle = result as TerminalHandle;
			running = true;
		}
	}

	function handleKeydown(e: KeyboardEvent): void {
		if (e.ctrlKey && e.key.toLowerCase() === 'c') {
			e.preventDefault();
			if (running) {
				write('^C', 'info');
				stopActive();
			}
			return;
		}
		if (e.key === 'Enter') {
			submit();
			return;
		}
		if (e.key === 'ArrowUp') {
			e.preventDefault();
			if (recall.length === 0) return;
			recallIndex = recallIndex === null ? recall.length - 1 : Math.max(0, recallIndex - 1);
			inputValue = recall[recallIndex];
			return;
		}
		if (e.key === 'ArrowDown') {
			e.preventDefault();
			if (recallIndex === null) return;
			const next = recallIndex + 1;
			if (next >= recall.length) {
				recallIndex = null;
				inputValue = '';
			} else {
				recallIndex = next;
				inputValue = recall[next];
			}
		}
	}

	// Closing the modal must not leave a `tail` WebSocket running in the background -
	// same cleanup discipline any connectLiveTail() caller needs. Also resets the
	// visible history, mirroring PatternsModal's "fetch fresh every open" pattern:
	// cheap, and there's no reason a closed-and-reopened terminal should look stale.
	$effect(() => {
		if (!open) {
			stopActive();
			lines = [];
			recall = [];
			recallIndex = null;
		}
	});

	onDestroy(stopActive);
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="icon-sm" title="Terminal">
				<TerminalIcon />
			</Button>
		{/snippet}
	</Dialog.Trigger>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>Terminal</Dialog.Title>
			<Dialog.Description>
				A flare.cli-styled command surface over this dashboard's own data - not a real shell. Type
				<code>help</code> to see what's available.
			</Dialog.Description>
		</Dialog.Header>
		<div bind:this={outputEl} class="bg-muted/30 h-96 overflow-y-auto rounded-md border p-3 font-mono text-xs">
			{#each lines as line, i (i)}
				<div class="{lineClass(line.kind)} whitespace-pre-wrap break-words">{line.text}</div>
			{/each}
			{#if lines.length === 0}
				<div class="text-muted-foreground space-y-2">
					<p>A flare.cli-styled command surface over this dashboard's own data. Try:</p>
					<div>
						{#each EXAMPLES as example (example)}
							<button
								type="button"
								class="hover:text-foreground block w-full text-left"
								onclick={() => useExample(example)}
							>
								<span class="select-none">flare&gt; </span>{example}
							</button>
						{/each}
					</div>
					<p>Or type <code>help</code> to see everything available.</p>
				</div>
			{/if}
		</div>
		<div class="flex items-center gap-2">
			<span class="text-muted-foreground font-mono text-xs">flare&gt;</span>
			<Input
				bind:ref={inputEl}
				bind:value={inputValue}
				onkeydown={handleKeydown}
				placeholder={running ? 'running - Ctrl+C to stop' : 'type a command...'}
				class="font-mono text-xs"
				autocomplete="off"
				spellcheck="false"
			/>
		</div>
	</Dialog.Content>
</Dialog.Root>
