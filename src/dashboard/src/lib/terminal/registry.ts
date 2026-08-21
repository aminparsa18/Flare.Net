// Command lookup + input tokenizing for the terminal modal. Adding a new command
// (either because flare.cli grew one, or because more of an existing one becomes
// mimicable) is: write a commands/<name>.ts implementing TerminalCommand, add it to
// COMMANDS below. No changes needed anywhere else - TerminalModal.svelte only ever
// talks to resolveCommand()/parseCommandLine().

import type { TerminalCommand } from './types';
import { tailCommand } from './commands/tail';
import { tracesCommand } from './commands/traces';
import { traceCommand } from './commands/trace';
import { metricsCommand } from './commands/metrics';
import { metricCommand } from './commands/metric';
import { ingestionCommand } from './commands/ingestion';
import { helpCommand } from './commands/help';
import { hostOnlyCommand } from './commands/unavailable';

// The 6 flare.cli commands that are inherently host-bound (docker compose
// lifecycle/env checks - see src/Flare.Cli/Commands/*.cs) plus `status` and `logs`,
// which shell out to `docker compose ps`/`docker compose logs` on the host. None of
// these can be answered from the browser; registering them as stubs (rather than
// leaving them out) keeps `help` honest instead of silently missing 8 of 14 real
// commands. Revisit `status` if the Resources page's opt-in DockerResources:ProxyUrl
// proxy ever grows an endpoint this could ride on.
const HOST_ONLY_COMMANDS: TerminalCommand[] = [
	hostOnlyCommand('start', 'Starts the docker compose stack on the host.'),
	hostOnlyCommand('stop', 'Stops the docker compose stack on the host.'),
	hostOnlyCommand('destroy', 'Tears down the docker compose stack on the host.'),
	hostOnlyCommand('update', 'Updates Flare on the host.'),
	hostOnlyCommand('doctor', "Checks the host's Docker/port prerequisites."),
	hostOnlyCommand('open', "Opens the dashboard in the host's default browser."),
	hostOnlyCommand('status', 'Shows docker compose service status from the host.'),
	hostOnlyCommand('logs', 'Shows raw container stdout from the host.')
];

// The remaining 6 of 14 - tail/traces/trace/metrics/metric/ingestion - all reach
// Flare.Api over HTTP/WebSocket the same way clicking around the dashboard already
// does, so (unlike the stubs above) they run for real here.
const COMMANDS: TerminalCommand[] = [
	tailCommand,
	tracesCommand,
	traceCommand,
	metricsCommand,
	metricCommand,
	ingestionCommand,
	helpCommand(() => COMMANDS),
	...HOST_ONLY_COMMANDS
];

export function listCommands(): readonly TerminalCommand[] {
	return COMMANDS;
}

export function resolveCommand(name: string): TerminalCommand | undefined {
	const lower = name.toLowerCase();
	return COMMANDS.find((c) => c.name === lower);
}

/**
 * Whitespace tokenizer with double-quote support - enough for `--search "foo bar"`.
 * No escape-sequence handling (no command here needs one); a lone unterminated quote
 * just runs to end of input rather than erroring, which is fine for a UI this small.
 */
export function parseCommandLine(input: string): string[] {
	const tokens: string[] = [];
	let current = '';
	let inQuotes = false;

	for (const ch of input.trim()) {
		if (ch === '"') {
			inQuotes = !inQuotes;
			continue;
		}
		if (ch === ' ' && !inQuotes) {
			if (current.length > 0) {
				tokens.push(current);
				current = '';
			}
			continue;
		}
		current += ch;
	}
	if (current.length > 0) tokens.push(current);

	return tokens;
}
