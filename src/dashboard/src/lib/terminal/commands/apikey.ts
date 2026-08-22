// `apikey` - mimics flare.cli's ApiKeyCreateCommand.cs (a branch off `apikey`, same as
// the real CLI) by calling the new $lib/ingest-keys-api.ts's createIngestApiKey() -
// POST /api/ingest-keys, admin-only on the backend. Only `create` - matching
// ApiKeyCreateCommand.cs's own deliberately-scoped surface (list/revoke endpoints exist
// but aren't wrapped by either front end yet).

import { createIngestApiKey } from '$lib/ingest-keys-api';
import type { TerminalCommand } from '../types';

export const apikeyCommand: TerminalCommand = {
	name: 'apikey',
	summary: 'Create an ingest API key.',
	usage: 'apikey create <NAME>',
	async run(args, term) {
		const [sub, name] = args;

		if (sub !== 'create') {
			term.writeLine(`apikey: expected a subcommand - 'create <NAME>'.`, 'error');
			return;
		}

		if (!name) {
			term.writeLine('apikey create: missing <NAME>.', 'error');
			return;
		}

		let response;
		try {
			response = await createIngestApiKey({ name });
		} catch (err) {
			term.writeLine(`apikey create: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const created = new Date(response.key.createdAt).toLocaleString();
		term.writeLine(`Created ingest key ${response.key.name} (id: ${response.key.id}, created: ${created}).`, 'output');
		term.writeLine(response.rawKey, 'output');
		term.writeLine('Copy this now - Flare never stores or shows the raw key again.', 'info');
	}
};
