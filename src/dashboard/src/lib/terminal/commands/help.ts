import type { TerminalCommand } from '../types';

// Takes a thunk rather than the command list directly - registry.ts builds this
// command as part of the same array it belongs to, so the array can't be passed in
// by value yet (it doesn't exist until the array literal finishes evaluating).
export function helpCommand(getCommands: () => readonly TerminalCommand[]): TerminalCommand {
	return {
		name: 'help',
		summary: 'Lists available commands.',
		usage: 'help',
		run(_args, term) {
			const commands = getCommands();
			const width = Math.max(...commands.map((c) => c.name.length));
			for (const c of commands) {
				term.writeLine(`${c.name.padEnd(width)}  ${c.summary}`, 'info');
			}
		}
	};
}
