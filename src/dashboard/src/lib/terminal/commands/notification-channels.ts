// `notification-channels` - mimics flare.cli's NotificationChannelsCommand.cs (a branch off
// `notification-channels`): list / create / update / delete / send-test, via the existing
// $lib/notification-channels-api.ts functions (the same /api/notification-channels/*
// endpoints the Settings > Notification channels page uses - no new backend surface).
//
// Option names, --type aliases, update's "only override what was passed" carry-forward
// (and clearing the other destination fields when --type changes) and delete's
// confirmation all follow the CLI. Delete's interactive prompt is the browser's confirm()
// dialog; `-y|--yes` skips it, same as the CLI.

import {
	createNotificationChannel,
	deleteNotificationChannel,
	getNotificationChannel,
	listNotificationChannels,
	sendTestNotificationChannel,
	updateNotificationChannel,
	type NotificationChannel,
	type NotificationChannelRequest,
	type NotificationChannelType
} from '$lib/notification-channels-api';
import type { TerminalCommand, TerminalWriter } from '../types';
import { formatTable } from './table';
import { UsageError } from './usage-error';

const VALID_TYPES = 'webhook, telegram, email, pagerduty, teams, discord, jira, incidentio, jsmops';

// Mirrors NotificationChannelTypeParsing.Normalize.
function normalizeType(type: string | undefined): NotificationChannelType | undefined {
	switch (type?.trim().toLowerCase()) {
		case 'webhook':
			return 'Webhook';
		case 'telegram':
			return 'Telegram';
		case 'email':
			return 'Email';
		case 'pagerduty':
		case 'pager-duty':
			return 'PagerDuty';
		case 'jira':
			return 'Jira';
		case 'jsmops':
		case 'jsm-ops':
		case 'jsm':
			return 'JsmOps';
		case 'incidentio':
		case 'incident.io':
		case 'incident-io':
			return 'IncidentIo';
		case 'teams':
		case 'msteams':
			return 'Teams';
		case 'discord':
			return 'Discord';
		default:
			return undefined;
	}
}

// Option flag -> request field. The destination fields (everything but description) are
// what update clears when --type changes.
type StringField = Exclude<keyof NotificationChannelRequest, 'name' | 'type' | 'sendResolved'>;

const STRING_OPTIONS: Record<string, StringField> = {
	'--description': 'description',
	'--webhook-url': 'webhookUrl',
	'--telegram-bot-token': 'telegramBotToken',
	'--telegram-chat-id': 'telegramChatId',
	'--email-to': 'emailTo',
	'--pagerduty-routing-key': 'pagerDutyRoutingKey',
	'--jira-base-url': 'jiraBaseUrl',
	'--jira-email': 'jiraEmail',
	'--jira-api-token': 'jiraApiToken',
	'--jira-project-key': 'jiraProjectKey',
	'--jira-issue-type': 'jiraIssueType',
	'--incidentio-token': 'incidentIoToken',
	'--jsmops-api-key': 'jsmOpsApiKey'
};

const DESTINATION_FIELDS = Object.values(STRING_OPTIONS).filter((f) => f !== 'description');

interface ParsedOptions {
	type?: string;
	rename?: string;
	yes: boolean;
	sendResolved?: boolean;
	fields: Partial<Record<StringField, string>>;
}

function parseOptions(args: string[], command: string, allowed: { type?: boolean; rename?: boolean; yes?: boolean; fields?: boolean }): ParsedOptions {
	const result: ParsedOptions = { yes: false, fields: {} };
	for (let i = 0; i < args.length; i++) {
		const arg = args[i];
		const value = () => {
			const v = args[++i];
			if (v === undefined) throw new UsageError(`${command}: ${arg} requires a value`);
			return v;
		};
		if (allowed.type && arg === '--type') result.type = value();
		else if (allowed.rename && arg === '--rename') result.rename = value();
		else if (allowed.yes && (arg === '-y' || arg === '--yes')) result.yes = true;
		else if (allowed.fields && arg === '--send-resolved') {
			const raw = value();
			if (raw.toLowerCase() !== 'true' && raw.toLowerCase() !== 'false') {
				throw new UsageError(`${command}: invalid --send-resolved '${raw}' - expected true or false`);
			}
			result.sendResolved = raw.toLowerCase() === 'true';
		} else if (allowed.fields && arg in STRING_OPTIONS) result.fields[STRING_OPTIONS[arg]] = value();
		else throw new UsageError(`${command}: unrecognized option '${arg}'`);
	}
	return result;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function requireId(id: string | undefined, command: string): string {
	if (!id) throw new UsageError(`${command}: missing <ID> - see \`help notification-channels\`.`);
	if (!GUID.test(id)) throw new UsageError(`${command}: '${id}' isn't a valid id (expected a GUID).`);
	return id;
}

// Mirrors NotificationChannelsListCommand.DestinationSummary / the Settings table's
// destinationSummary: never a raw secret in full.
function destinationSummary(c: NotificationChannel): string {
	switch (c.type) {
		case 'Webhook':
		case 'Teams':
		case 'Discord':
		case 'IncidentIo':
			return c.webhookUrl;
		case 'Telegram':
			return c.telegramChatId ? `chat ${c.telegramChatId}` : '';
		case 'Email':
			return c.emailTo;
		case 'PagerDuty':
			return c.pagerDutyRoutingKey ? `${c.pagerDutyRoutingKey.slice(0, 6)}…` : '';
		case 'Jira':
			return c.jiraProjectKey ? `${c.jiraBaseUrl} (${c.jiraProjectKey})` : '';
		case 'JsmOps':
			return c.jsmOpsApiKey ? `${c.jsmOpsApiKey.slice(0, 6)}…` : '';
		default:
			return '';
	}
}

async function runList(term: TerminalWriter): Promise<void> {
	let channels: NotificationChannel[];
	try {
		channels = (await listNotificationChannels()).channels;
	} catch (err) {
		term.writeLine(`notification-channels list: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	if (channels.length === 0) {
		term.writeLine('No notification channels configured.', 'info');
		return;
	}

	const rows = channels.map((c) => [c.name, c.type, destinationSummary(c), c.sendResolved ? 'yes' : 'no', c.id]);
	for (const line of formatTable(['NAME', 'TYPE', 'DESTINATION', 'RESOLVED', 'ID'], rows)) term.writeLine(line, 'output');
}

async function runCreate(rest: string[], term: TerminalWriter): Promise<void> {
	const [name, ...flags] = rest;
	if (!name || name.startsWith('-')) throw new UsageError('notification-channels create: missing <NAME> - see `help notification-channels`.');
	if (name.trim().length === 0) throw new UsageError("notification-channels create: NAME can't be empty.");

	const opts = parseOptions(flags, 'notification-channels create', { type: true, fields: true });
	const type = normalizeType(opts.type);
	if (!type) throw new UsageError(`✗ --type is required and must be one of: ${VALID_TYPES}.`);

	const request: NotificationChannelRequest = { name, type, ...opts.fields, sendResolved: opts.sendResolved };

	let channel;
	try {
		channel = await createNotificationChannel(request);
	} catch (err) {
		term.writeLine(`✗ ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}
	term.writeLine(`✓ Created ${channel.type} channel ${channel.name} (id: ${channel.id}).`, 'output');
}

async function runUpdate(rest: string[], term: TerminalWriter): Promise<void> {
	const [rawId, ...flags] = rest;
	const id = requireId(rawId, 'notification-channels update');
	const opts = parseOptions(flags, 'notification-channels update', { type: true, rename: true, fields: true });

	let normalizedType: NotificationChannelType | undefined;
	if (opts.type !== undefined) {
		normalizedType = normalizeType(opts.type);
		if (!normalizedType) throw new UsageError(`✗ --type must be one of: ${VALID_TYPES}.`);
	}

	let existing: NotificationChannel | null;
	try {
		existing = await getNotificationChannel(id);
	} catch (err) {
		term.writeLine(
			/\b404\b/.test(String(err)) ? `✗ No notification channel with id ${id}.` : `✗ ${err instanceof Error ? err.message : String(err)}`,
			'error'
		);
		return;
	}

	const effectiveType = normalizedType ?? existing.type;
	const typeChanged = effectiveType !== existing.type;

	// Carries an existing destination field forward only when the type is unchanged - a
	// type change clears the others so a stale field can't fail the API's exclusivity check.
	const request: NotificationChannelRequest = {
		name: opts.rename ?? existing.name,
		description: opts.fields.description ?? existing.description,
		type: effectiveType,
		sendResolved: opts.sendResolved ?? existing.sendResolved
	};
	for (const field of DESTINATION_FIELDS) {
		const provided = opts.fields[field];
		(request as unknown as Record<string, string | undefined>)[field] = provided ?? (typeChanged ? undefined : (existing as unknown as Record<string, string>)[field]);
	}

	let updated;
	try {
		updated = await updateNotificationChannel(id, request);
	} catch (err) {
		term.writeLine(`✗ ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}
	term.writeLine(`✓ Updated ${updated.type} channel ${updated.name} (id: ${id}).`, 'output');
}

async function runDelete(rest: string[], term: TerminalWriter): Promise<void> {
	const [rawId, ...flags] = rest;
	const id = requireId(rawId, 'notification-channels delete');
	const opts = parseOptions(flags, 'notification-channels delete', { yes: true });

	let existing: NotificationChannel | null;
	try {
		existing = await getNotificationChannel(id);
	} catch (err) {
		term.writeLine(
			/\b404\b/.test(String(err)) ? `✗ No notification channel with id ${id}.` : `✗ ${err instanceof Error ? err.message : String(err)}`,
			'error'
		);
		return;
	}

	if (!opts.yes) {
		const confirmed = confirm(`Delete notification channel "${existing.name}"? Any alert rule referencing it will silently drop it. Continue?`);
		if (!confirmed) {
			term.writeLine('Aborted - nothing was removed.', 'info');
			return;
		}
	}

	try {
		await deleteNotificationChannel(id);
	} catch (err) {
		term.writeLine(`✗ ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}
	term.writeLine(`✓ Deleted channel ${existing.name}.`, 'output');
}

async function runSendTest(rest: string[], term: TerminalWriter): Promise<void> {
	const id = requireId(rest[0], 'notification-channels send-test');

	let result;
	try {
		result = await sendTestNotificationChannel(id);
	} catch (err) {
		term.writeLine(
			/\b404\b/.test(String(err)) ? `✗ No notification channel with id ${id}.` : `✗ ${err instanceof Error ? err.message : String(err)}`,
			'error'
		);
		return;
	}

	if (result.success) term.writeLine('✓ Test notification sent.', 'output');
	else term.writeLine(`✗ Send failed: ${result.error}`, 'error');
}

export const notificationChannelsCommand: TerminalCommand = {
	name: 'notification-channels',
	summary: 'List/create/update/delete/test notification channels.',
	usage:
		'notification-channels list | create <NAME> --type <type> [--description <text>] [--webhook-url <url>] [--telegram-bot-token <t>] [--telegram-chat-id <id>] [--email-to <addr>] [--pagerduty-routing-key <k>] [--jira-base-url <url>] [--jira-email <e>] [--jira-api-token <t>] [--jira-project-key <k>] [--jira-issue-type <n>] [--incidentio-token <t>] [--jsmops-api-key <k>] [--send-resolved true|false] | update <ID> [--rename <name>] [--type <type>] [same options as create] | delete <ID> [-y|--yes] | send-test <ID>',
	async run(args, term) {
		const [sub, ...rest] = args;
		try {
			switch (sub) {
				case 'list':
					return await runList(term);
				case 'create':
					return await runCreate(rest, term);
				case 'update':
					return await runUpdate(rest, term);
				case 'delete':
					return await runDelete(rest, term);
				case 'send-test':
					return await runSendTest(rest, term);
				default:
					throw new UsageError(`notification-channels: expected a subcommand - 'list', 'create <NAME>', 'update <ID>', 'delete <ID>' or 'send-test <ID>'.`);
			}
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
		}
	}
};
