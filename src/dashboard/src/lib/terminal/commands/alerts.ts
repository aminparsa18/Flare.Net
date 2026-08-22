// `alerts` - mimics flare.cli's AlertsListCommand/AlertsTestCommand (a branch off
// `alerts`, same as the real CLI) by calling $lib/alerts-api.ts's existing
// listAlertRules()/testAlertRule() - both already exactly GET /api/alerts and
// POST /api/alerts/{id}/test, no new backend surface. First command in this terminal to
// need args[0] sub-dispatch (`list` / `test <id>`) - every other command here is a
// single verb, so there's no shared sub-dispatch helper to reuse yet.

import { listAlertRules, testAlertRule, type AlertRule } from '$lib/alerts-api';
import type { TerminalCommand, TerminalWriter } from '../types';

function formatWindow(seconds: number): string {
	if (seconds >= 3600 && seconds % 3600 === 0) return `${seconds / 3600}h`;
	if (seconds >= 60 && seconds % 60 === 0) return `${seconds / 60}m`;
	return `${seconds}s`;
}

// Mirrors Flare.Cli's AlertsCommand.cs DescribeChannel.
function describeChannel(rule: AlertRule): string {
	if (rule.webhookUrl) return 'Webhook';
	if (rule.telegramBotToken && rule.telegramChatId) return 'Telegram';
	if (rule.emailTo) return 'Email';
	return 'none';
}

function formatRow(rule: AlertRule): string {
	const enabled = (rule.enabled ? '✓' : '✗').padEnd(3);
	const comparator = rule.threshold.comparator === 'LessThan' ? '<' : '>=';
	const threshold = `${comparator} ${rule.threshold.count}`.padEnd(9);
	const window = formatWindow(rule.windowSeconds).padEnd(6);
	const channel = describeChannel(rule).padEnd(9);
	const name = rule.name.padEnd(24).slice(0, 24);
	return `${name}${enabled}${threshold}${window}${channel}${rule.id}`;
}

async function runList(term: TerminalWriter): Promise<void> {
	let rules: AlertRule[];
	try {
		rules = (await listAlertRules()).rules;
	} catch (err) {
		term.writeLine(`alerts list: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	if (rules.length === 0) {
		term.writeLine('No alert rules configured.', 'info');
		return;
	}

	term.writeLine('NAME                    EN  THRESHOLD  WINDOW  CHANNEL  ID', 'info');
	for (const rule of rules) {
		term.writeLine(formatRow(rule), 'output');
	}
}

async function runTest(id: string | undefined, term: TerminalWriter): Promise<void> {
	if (!id) {
		term.writeLine('alerts test: missing <ID> - see `help alerts`.', 'error');
		return;
	}

	let result;
	try {
		result = await testAlertRule(id);
	} catch (err) {
		term.writeLine(`alerts test: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	term.writeLine(`Would fire: ${result.wouldFire ? 'yes' : 'no'}`, result.wouldFire ? 'output' : 'info');
	term.writeLine(`Observed count: ${result.observedCount} (window: ${result.windowSeconds}s)`, 'output');
	term.writeLine(
		`Evaluated at ${new Date(result.evaluatedAt).toLocaleTimeString()} - cooldown untouched, no notification sent.`,
		'info'
	);
}

export const alertsCommand: TerminalCommand = {
	name: 'alerts',
	summary: 'List/test saved alert rules.',
	usage: 'alerts list | alerts test <ID>',
	async run(args, term) {
		const [sub, ...rest] = args;
		switch (sub) {
			case 'list':
				return runList(term);
			case 'test':
				return runTest(rest[0], term);
			default:
				term.writeLine(`alerts: expected a subcommand - 'list' or 'test <ID>'.`, 'error');
		}
	}
};
