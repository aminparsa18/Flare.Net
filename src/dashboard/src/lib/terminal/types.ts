// Shared shapes for the dashboard's terminal modal (TerminalModal.svelte) - a
// flare.cli-*styled* command surface, not a real shell. Every TerminalCommand.run()
// is expected to reach existing, already-authenticated dashboard APIs (see
// commands/tail.ts calling the same connectLiveTail() the Logs Explorer uses) rather
// than execute anything - there is deliberately no code path here that spawns a
// process or reaches the host. See commands/unavailable.ts for the commands that
// can't be mimicked this way (start/stop/destroy/update/doctor/open/status/logs).

export type TerminalLineKind = 'input' | 'output' | 'error' | 'info';

export interface TerminalLine {
	kind: TerminalLineKind;
	text: string;
}

/** Passed to a command's run() so it can stream output without owning the modal's own state. */
export interface TerminalWriter {
	writeLine(text: string, kind?: TerminalLineKind): void;
}

/**
 * Returned by a command whose work outlives a single run() call (currently only
 * `tail`, which streams over a WebSocket until stopped). TerminalModal calls
 * cancel() on Ctrl+C, when a new command is submitted, and on its own unmount -
 * see that file's cleanup discipline for why this matters (an uncancelled `tail`
 * would otherwise leak an open WebSocket).
 */
export interface TerminalHandle {
	cancel(): void;
}

export interface TerminalCommand {
	/** Lowercase, e.g. "tail" - matched case-insensitively against the first token of input. */
	name: string;
	/** One-line summary shown by `help`. */
	summary: string;
	/** Full usage line shown by `help <name>`, e.g. "tail [-s|--service <name>] [-l|--level <level>] [--trace-id <id>] [--search <text>]". */
	usage: string;
	/**
	 * Runs the command. Non-streaming commands do their work and return (or resolve);
	 * streaming commands return a TerminalHandle immediately and keep writing via
	 * `term` until cancel()ed.
	 */
	run(args: string[], term: TerminalWriter): TerminalHandle | Promise<void> | void;
}
