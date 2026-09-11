// `token` - self-service personal access tokens (ADR-0019). Unlike every other command in
// this registry, this one has NO flare.cli counterpart: `flare apikey create` works
// unauthenticated against localhost because ingest keys aren't tied to a user, but a PAT
// authenticates *as whoever creates it* - the standalone CLI process has no logged-in
// identity to mint one for. This terminal modal runs inside an actual authenticated
// browser tab, though, and calls the same $lib/personal-access-tokens-api.ts functions
// riding that tab's own session cookie the same way every other command here already
// does - so `create`/`list`/`revoke` all work for real, they just have no `flare token`
// equivalent to mimic. See registry.ts's own comment for where this fits among the rest.

import { createAccessToken, listAccessTokens, revokeAccessToken } from '$lib/personal-access-tokens-api';
import type { TerminalCommand } from '../types';

export const tokenCommand: TerminalCommand = {
	name: 'token',
	summary: 'Manage your own personal access tokens.',
	usage: 'token create <NAME> [--expires-in-days <N>] | token list | token revoke <ID>',
	async run(args, term) {
		const [sub, ...rest] = args;

		if (sub === 'create') {
			const [name, ...flags] = rest;
			if (!name) {
				term.writeLine('token create: missing <NAME>.', 'error');
				return;
			}

			let expiresInDays: number | null = null;
			const flagIndex = flags.indexOf('--expires-in-days');
			if (flagIndex !== -1) {
				const raw = flags[flagIndex + 1];
				const parsed = raw ? Number.parseInt(raw, 10) : NaN;
				if (!Number.isFinite(parsed) || parsed < 1) {
					term.writeLine('token create: --expires-in-days needs a positive integer.', 'error');
					return;
				}
				expiresInDays = parsed;
			}

			let response;
			try {
				response = await createAccessToken({ name, expiresInDays });
			} catch (err) {
				term.writeLine(`token create: ${err instanceof Error ? err.message : String(err)}`, 'error');
				return;
			}

			const created = new Date(response.token.createdAt).toLocaleString();
			const expiry = response.token.expiresAt ? `, expires: ${new Date(response.token.expiresAt).toLocaleString()}` : ' (never expires)';
			term.writeLine(`Created access token ${response.token.name} (id: ${response.token.id}, created: ${created}${expiry}).`, 'output');
			term.writeLine(response.rawToken, 'output');
			term.writeLine('Copy this now - Flare never stores or shows the raw token again.', 'info');
			return;
		}

		if (sub === 'list') {
			let tokens;
			try {
				tokens = await listAccessTokens();
			} catch (err) {
				term.writeLine(`token list: ${err instanceof Error ? err.message : String(err)}`, 'error');
				return;
			}

			if (tokens.length === 0) {
				term.writeLine('No access tokens.', 'info');
				return;
			}

			for (const t of tokens) {
				const status = t.revokedAt ? 'revoked' : t.isActive ? 'active' : 'expired';
				term.writeLine(`${t.id}  ${t.name}  [${status}]  created: ${new Date(t.createdAt).toLocaleString()}`, 'output');
			}
			return;
		}

		if (sub === 'revoke') {
			const [id] = rest;
			if (!id) {
				term.writeLine('token revoke: missing <ID>.', 'error');
				return;
			}

			try {
				await revokeAccessToken(id);
			} catch (err) {
				term.writeLine(`token revoke: ${err instanceof Error ? err.message : String(err)}`, 'error');
				return;
			}

			term.writeLine(`Revoked access token ${id}.`, 'output');
			return;
		}

		term.writeLine(`token: expected a subcommand - 'create <NAME>', 'list', or 'revoke <ID>'.`, 'error');
	}
};
