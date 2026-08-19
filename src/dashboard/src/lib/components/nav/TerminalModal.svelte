<script lang="ts">
	// A flare.cli-*styled* command surface over this dashboard's own APIs - not a real
	// shell, no process execution, no host access. Every command in $lib/terminal
	// reaches an already-authenticated dashboard API the same way clicking around the
	// UI would; see $lib/terminal/registry.ts for how new commands slot in.
	//
	// Self-contained trigger + Dialog in one component (same shape as
	// PatternsModal.svelte), not the lifted-state pattern AppNav/CommandPalette use for
	// commandPaletteOpen - nothing else needs to open this modal.
	//
	// Styled to actually read as a terminal window rather than a form dialog: a fixed
	// dark palette (deliberately not following the app's own light/dark mode - real
	// terminal emulators don't either), a titlebar with traffic-light dots, and the
	// prompt/input pinned at the bottom outside the scrolling output rather than boxed
	// separately below it.
	import { tick, onDestroy } from 'svelte';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
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
				return 'text-red-400';
			case 'input':
				return 'text-neutral-400';
			case 'info':
				return 'text-neutral-500';
			default:
				return 'text-neutral-100';
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

	// Ready to type the moment it opens - a terminal that opens without focus in its
	// own input isn't acting like one.
	$effect(() => {
		if (open) void tick().then(() => inputEl?.focus());
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
	<Dialog.Content
		showCloseButton={false}
		class="flex h-[28rem] w-full flex-col gap-0 overflow-hidden rounded-lg border border-neutral-800 bg-neutral-950 p-0 text-neutral-200 ring-0 sm:max-w-2xl"
	>
		<div class="flex shrink-0 items-center gap-2 border-b border-neutral-800 bg-neutral-900/60 px-3 py-2">
			<Dialog.Close class="group inline-flex cursor-pointer items-center" aria-label="Close terminal">
				<span class="block size-2.5 rounded-full bg-red-500/90 transition-colors group-hover:bg-red-400"></span>
			</Dialog.Close>
			<span class="size-2.5 rounded-full bg-yellow-500/70"></span>
			<span class="size-2.5 rounded-full bg-green-500/70"></span>
			<Dialog.Title class="flex-1 truncate text-center font-mono text-[0.7rem] font-normal text-neutral-500">
				flare — terminal
			</Dialog.Title>
			<span class="w-[46px]" aria-hidden="true"></span>
		</div>
		<div bind:this={outputEl} class="flex-1 overflow-y-auto px-3 py-2 font-mono text-xs leading-relaxed">
			{#if lines.length === 0}
				<div class="space-y-2 text-neutral-500">
					<p>A flare.cli-styled command surface over this dashboard's own data. Try:</p>
					<div>
						{#each EXAMPLES as example (example)}
							<button
								type="button"
								class="block w-full text-left text-neutral-400 hover:text-neutral-100"
								onclick={() => useExample(example)}
							>
								<span class="text-emerald-500">flare&gt;</span>
								{example}
							</button>
						{/each}
					</div>
					<p>Or type <span class="text-neutral-300">help</span> to see everything available.</p>
				</div>
			{/if}
			{#each lines as line, i (i)}
				<div class="{lineClass(line.kind)} whitespace-pre-wrap break-words">{line.text}</div>
			{/each}
		</div>
		<div class="flex shrink-0 items-center gap-1.5 border-t border-neutral-900 px-3 py-2">
			<span class="shrink-0 font-mono text-xs text-emerald-500">flare&gt;</span>
			<input
				bind:this={inputEl}
				bind:value={inputValue}
				onkeydown={handleKeydown}
				placeholder={running ? 'running — Ctrl+C to stop' : ''}
				class="flex-1 bg-transparent font-mono text-xs text-neutral-100 outline-none placeholder:text-neutral-600"
				autocomplete="off"
				spellcheck="false"
			/>
		</div>
	</Dialog.Content>
</Dialog.Root>
