// Stub for the flare.cli commands this terminal can't mimic - they're host-bound
// (docker compose lifecycle, or reading the host's Docker daemon directly; see
// registry.ts's HOST_ONLY_COMMANDS for which and why). Registered rather than
// omitted so `help` stays honest and typing e.g. `stop` explains itself instead of
// silently no-oping or looking like a bug.

import type { TerminalCommand } from '../types';

export function hostOnlyCommand(name: string, summary: string): TerminalCommand {
	return {
		name,
		summary: `${summary} (not available here)`,
		usage: name,
		run(_args, term) {
			term.writeLine(
				`${name}: not available from the dashboard - this needs access to the host machine. Run \`flare ${name}\` in a real terminal instead.`,
				'error'
			);
		}
	};
}
