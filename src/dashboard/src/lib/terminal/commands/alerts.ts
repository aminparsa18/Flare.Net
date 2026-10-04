// `alerts` - mimics flare.cli's AlertsCommand.cs (a branch off `alerts`, same as the real
// CLI): list / history / test / send-test / export / import, each calling the matching
// existing $lib/alerts-api.ts function (all already exactly the /api/alerts/* endpoints the
// CLI hits, no new backend surface).
//
// Where a browser can't do what the CLI does, the divergence is the destination of the
// bytes, not the data: `export` prints the JSON (or, with -o, downloads it as a file named
// <FILE>), and `import` opens a file picker instead of reading a host path.

import {
	exportAlertRules,
	getAlertHistory,
	importAlertRules,
	listAlertRules,
	sendTestAlertRule,
	testAlertRule,
	type AlertRule,
	type AnomalyCondition
} from '$lib/alerts-api';
import { downloadBlob } from '$lib/logs/export';
import { formatTimeOfDay } from '$lib/time/format';
import type { TerminalCommand, TerminalWriter } from '../types';
import { formatG4, formatNumber, formatTable } from './table';
import { UsageError } from './usage-error';

function formatWindow(seconds: number): string {
	if (seconds >= 3600 && seconds % 3600 === 0) return `${seconds / 3600}h`;
	if (seconds >= 60 && seconds % 60 === 0) return `${seconds / 60}m`;
	return `${seconds}s`;
}

// Mirrors Flare.Cli's AlertsCommand.cs DescribeChannel.
function describeChannel(rule: AlertRule): string {
	if (rule.channelIds.length > 0) return rule.channelIds.length === 1 ? '1 channel' : `${rule.channelIds.length} channels`;
	if (rule.webhookUrl.trim()) return 'Webhook';
	if (rule.telegramBotToken.trim() && rule.telegramChatId.trim()) return 'Telegram';
	if (rule.emailTo.trim()) return 'Email';
	if (rule.pagerDutyRoutingKey.trim()) return 'PagerDuty';
	return 'none';
}

/** E.g. "|z| >= 3 vs 7 days" - an anomaly rule has no fixed threshold to show. Mirrors AlertsListCommand.DescribeAnomaly. */
function describeAnomaly(anomaly: AnomalyCondition): string {
	const z = anomaly.direction === 'Above' ? 'z >=' : anomaly.direction === 'Below' ? 'z <= -' : '|z| >=';
	const unit = anomaly.seasonality === 'Weekly' ? 'weeks' : 'days';
	return `${z}${anomaly.direction === 'Below' ? '' : ' '}${anomaly.zScoreThreshold} vs ${anomaly.baselinePeriods} ${unit}`;
}

function describeThreshold(rule: AlertRule): string {
	if (rule.conditionKind === 'Anomaly' && rule.anomalyCondition) return describeAnomaly(rule.anomalyCondition);
	if (rule.conditionKind === 'SloBurnRate' && rule.sloCondition) {
		const slo = rule.sloCondition;
		return `burn >= ${slo.burnRateThreshold}x over ${formatWindow(slo.longWindowSeconds)} and ${formatWindow(slo.shortWindowSeconds)}`;
	}
	return `${rule.threshold.comparator === 'LessThan' ? '<' : '>='} ${rule.threshold.count}`;
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

	const rows = rules.map((rule) => [rule.name, rule.enabled ? '✓' : '✗', describeThreshold(rule), formatWindow(rule.windowSeconds), describeChannel(rule), rule.id]);
	for (const line of formatTable(['NAME', 'ENABLED', 'THRESHOLD', 'WINDOW', 'CHANNEL', 'ID'], rows)) {
		term.writeLine(line, 'output');
	}
}

async function runHistory(rest: string[], term: TerminalWriter): Promise<void> {
	const [id, ...flags] = rest;
	if (!id) {
		term.writeLine('alerts history: missing <ID> - see `help alerts`.', 'error');
		return;
	}

	let limit = 20;
	for (let i = 0; i < flags.length; i++) {
		if (flags[i] === '--limit') {
			const raw = flags[++i];
			const value = Number.parseInt(raw ?? '', 10);
			if (!Number.isFinite(value)) {
				term.writeLine(`alerts history: invalid --limit '${raw ?? ''}'`, 'error');
				return;
			}
			limit = value;
		} else {
			term.writeLine(`alerts history: unrecognized option '${flags[i]}'`, 'error');
			return;
		}
	}

	let events;
	try {
		events = (await getAlertHistory(id, Math.min(Math.max(limit, 1), 200))).events;
	} catch (err) {
		term.writeLine(`alerts history: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	if (events.length === 0) {
		term.writeLine('This rule has never fired.', 'info');
		return;
	}

	const ordered = [...events].sort((a, b) => b.firedAt.localeCompare(a.firedAt));
	const rows = ordered.map((e) => [
		e.firedAt.slice(0, 19).replace('T', ' '),
		e.resolved ? 'resolved' : 'fired',
		e.observedValue !== undefined ? formatG4(e.observedValue) : String(e.observedCount),
		e.thresholdValue !== undefined ? formatG4(e.thresholdValue) : String(e.thresholdCount),
		e.notificationStatus
	]);
	for (const line of formatTable(['WHEN (UTC)', 'EVENT', 'OBSERVED', 'THRESHOLD', 'NOTIFICATION'], rows)) {
		term.writeLine(line, 'output');
	}

	for (const e of ordered.filter((e) => e.aiSummary)) {
		term.writeLine('', 'output');
		term.writeLine(`AI summary (${e.firedAt.slice(11, 19)}Z, ${e.aiModel})`, 'info');
		term.writeLine(e.aiSummary, 'output');
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

	// Same branch order as AlertsTestCommand.cs's switch.
	if (result.noData) {
		term.writeLine(`No data: the condition matched nothing in the last ${result.windowSeconds}s (absent-data alerting)`, 'output');
	} else if (result.insufficientData) {
		term.writeLine(
			`Insufficient data: only ${result.dataPointCount ?? 0} data point(s) in the last ${result.windowSeconds}s - below the rule's minimum, so it can't fire`,
			'output'
		);
	} else if (result.conditionKind === 'Anomaly' && result.zScore !== undefined && result.baselineMean !== undefined) {
		const z = `${result.zScore >= 0 ? '+' : '-'}${Math.abs(result.zScore).toFixed(2)}`;
		term.writeLine(
			`Observed: ${formatNumber(result.observedValue)} vs baseline mean ${formatNumber(result.baselineMean)} (z = ${z}, ${result.baselineSampleCount} baseline windows, window: ${result.windowSeconds}s)`,
			'output'
		);
	} else if (result.conditionKind === 'SloBurnRate') {
		term.writeLine(
			`Burn rate over the long window: ${formatNumber(result.observedValue)}x (window: ${result.windowSeconds}s; fires only if the short window is burning too)`,
			'output'
		);
	} else if (result.conditionKind === 'Anomaly') {
		term.writeLine(
			`Not enough history: only ${result.baselineSampleCount} baseline window(s) had data - an anomaly rule needs at least 3 before it can fire`,
			'output'
		);
	} else {
		term.writeLine(`Observed count: ${result.observedCount} (window: ${result.windowSeconds}s)`, 'output');
	}

	term.writeLine(`Evaluated at ${formatTimeOfDay(result.evaluatedAt, 'ms')} - cooldown untouched, no notification sent.`, 'info');
}

async function runSendTest(id: string | undefined, term: TerminalWriter): Promise<void> {
	if (!id) {
		term.writeLine('alerts send-test: missing <ID> - see `help alerts`.', 'error');
		return;
	}

	let result;
	try {
		result = await sendTestAlertRule(id);
	} catch (err) {
		term.writeLine(`alerts send-test: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	if (result.success) {
		term.writeLine('✓ Test notification sent.', 'output');
	} else {
		term.writeLine(`✗ Send failed: ${result.error}`, 'error');
	}
}

/** Last path segment - a browser download can only name the file, not pick a directory. */
function fileNameOf(path: string): string {
	return path.split(/[\\/]/).filter((part) => part.length > 0).pop() ?? path;
}

async function runExport(rest: string[], term: TerminalWriter): Promise<void> {
	let output: string | undefined;
	for (let i = 0; i < rest.length; i++) {
		if (rest[i] === '-o' || rest[i] === '--output') {
			output = rest[++i];
			if (output === undefined) {
				term.writeLine(`alerts export: ${rest[i - 1]} requires a value`, 'error');
				return;
			}
		} else {
			term.writeLine(`alerts export: unrecognized option '${rest[i]}'`, 'error');
			return;
		}
	}

	let document;
	try {
		document = await exportAlertRules();
	} catch (err) {
		term.writeLine(`alerts export: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	const json = JSON.stringify(document, null, 2);
	if (output) {
		const name = fileNameOf(output);
		downloadBlob(new Blob([json + '\n'], { type: 'application/json;charset=utf-8' }), name);
		term.writeLine(`✓ Exported ${document.rules.length} rule(s) to ${name}`, 'output');
		return;
	}
	for (const line of json.split('\n')) term.writeLine(line, 'output');
}

/** Opens the browser's file picker and resolves with the chosen file's text, or null if cancelled. Must be called synchronously from the Enter keypress (a picker needs a user gesture). */
function pickTextFile(): Promise<{ name: string; text: string } | null> {
	return new Promise((resolve) => {
		const input = document.createElement('input');
		input.type = 'file';
		input.accept = '.json,application/json';
		input.addEventListener('change', async () => {
			const file = input.files?.[0];
			resolve(file ? { name: file.name, text: await file.text() } : null);
		});
		input.addEventListener('cancel', () => resolve(null));
		input.click();
	});
}

async function runImport(rest: string[], term: TerminalWriter): Promise<void> {
	let dryRun = false;
	for (const arg of rest) {
		if (arg === '--dry-run') dryRun = true;
		else if (arg.startsWith('-')) {
			term.writeLine(`alerts import: unrecognized option '${arg}'`, 'error');
			return;
		}
	}

	// Called before any await so the picker still counts as a user-gesture-initiated action.
	const picked = pickTextFile();
	term.writeLine('Choose the JSON file produced by `alerts export`...', 'info');
	const file = await picked;
	if (!file) {
		term.writeLine('alerts import: no file chosen.', 'error');
		return;
	}

	let document: unknown;
	try {
		document = JSON.parse(file.text);
	} catch {
		term.writeLine(`✗ ${file.name} isn't valid JSON.`, 'error');
		return;
	}

	let result;
	try {
		result = await importAlertRules(document, dryRun);
	} catch (err) {
		term.writeLine(`alerts import: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	const rows = result.items.map((item) => [item.name, item.outcome, item.message ?? '']);
	for (const line of formatTable(['RULE', 'OUTCOME', 'DETAIL'], rows)) term.writeLine(line, 'output');
	const verb = dryRun ? 'would create' : 'created';
	term.writeLine(
		`${verb} ${result.created}, skipped ${result.skipped}, ${result.errors} error(s)${dryRun ? ' (dry run)' : ''}`,
		result.errors > 0 ? 'error' : 'output'
	);
}

export const alertsCommand: TerminalCommand = {
	name: 'alerts',
	summary: 'List/inspect/test/export/import saved alert rules.',
	usage:
		'alerts list | alerts history <ID> [--limit <count>] | alerts test <ID> | alerts send-test <ID> | alerts export [-o|--output <name>] | alerts import [<FILE>] [--dry-run]',
	async run(args, term) {
		const [sub, ...rest] = args;
		try {
			switch (sub) {
				case 'list':
					return await runList(term);
				case 'history':
					return await runHistory(rest, term);
				case 'test':
					return await runTest(rest[0], term);
				case 'send-test':
					return await runSendTest(rest[0], term);
				case 'export':
					return await runExport(rest, term);
				case 'import':
					return await runImport(rest, term);
				default:
					throw new UsageError(`alerts: expected a subcommand - 'list', 'history <ID>', 'test <ID>', 'send-test <ID>', 'export' or 'import'.`);
			}
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
		}
	}
};
