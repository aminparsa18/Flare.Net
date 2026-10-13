#!/usr/bin/env node
// Smoke test for the built dashboard image's static assets (CI: .github/workflows/dashboard-image-smoke.yml).
//
// adapter-node 6 serves a closed set of files recorded at build time and sends the recorded
// size/etag verbatim, so a file rewritten after the build (apply-base-path.mjs does this at every
// container start) is served with a stale Content-Length unless the tables are refreshed. Unit
// tests can't see that; only a running server can. For every _app/**/*.js|css under --dir this
// requests <url><base>/<file> as identity, gzip and br and checks: 200, Content-Length present and
// equal to the bytes received, decoded body free of the build-time placeholder.
//
// Usage: node scripts/smoke-dashboard-assets.mjs --url http://127.0.0.1:3000 --base /flare --dir <client dir>
import { request } from 'node:http';
import { readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';
import { parseArgs } from 'node:util';

const { values } = parseArgs({
	options: { url: { type: 'string' }, base: { type: 'string', default: '' }, dir: { type: 'string' } }
});
if (!values.url || !values.dir) {
	console.error('usage: smoke-dashboard-assets.mjs --url <origin> [--base /prefix] --dir <build client dir>');
	process.exit(2);
}
const base = values.base.replace(/\/+$/, '');

function* walk(dir) {
	for (const entry of readdirSync(dir)) {
		const full = join(dir, entry);
		if (statSync(full).isDirectory()) yield* walk(full);
		else yield full;
	}
}

// Raw http, not fetch: fetch transparently decodes, which hides the encoded length we need to compare.
function get(path, encoding) {
	return new Promise((resolve, reject) => {
		// A Content-Length longer than the body (the bug this guards) leaves the response open
		// forever, so a timeout is itself a failure, not a hang.
		const req = request(values.url + path, { headers: { 'accept-encoding': encoding }, timeout: 5000 }, (res) => {
			const chunks = [];
			res.on('data', (c) => chunks.push(c));
			res.on('end', () => resolve({ res, body: Buffer.concat(chunks) }));
		});
		req.on('timeout', () => req.destroy(new Error('timed out (response never completed)')));
		req.on('error', reject);
		req.end();
	});
}

const decoders = { identity: (b) => b, gzip: gunzipSync, br: brotliDecompressSync };
const root = join(values.dir, base);
const files = [...walk(join(root, '_app'))].filter((f) => /\.(js|css)$/.test(f));
if (files.length === 0) {
	console.error(`no _app/**/*.js|css files under ${root}`);
	process.exit(2);
}

const failures = [];
let checks = 0;
for (const file of files) {
	const path = `${base}/${relative(root, file).split(sep).join('/')}`;
	for (const encoding of Object.keys(decoders)) {
		checks++;
		let response;
		try {
			response = await get(path, encoding);
		} catch (e) {
			failures.push(`${encoding.padEnd(8)} ${path}: ${e.message}`);
			continue;
		}
		const { res, body } = response;
		const declared = res.headers['content-length'];
		const problems = [];
		if (res.statusCode !== 200) problems.push(`status ${res.statusCode}`);
		else {
			if (declared === undefined) problems.push('no Content-Length');
			else if (Number(declared) !== body.length) problems.push(`Content-Length ${declared} != body ${body.length}`);
			const served = res.headers['content-encoding'] ?? 'identity';
			if (encoding !== 'identity' && served !== encoding) continue; // no precompressed variant for this file
			try {
				if (decoders[encoding](body).includes('__FLARE_BASE_PATH__')) problems.push('placeholder still present');
			} catch (e) {
				problems.push(`undecodable ${encoding} body (${e.message})`);
			}
		}
		if (problems.length) failures.push(`${encoding.padEnd(8)} ${path}: ${problems.join('; ')}`);
	}
}

console.log(`${files.length} files, ${checks} requests, ${failures.length} failures`);
for (const f of failures) console.error(`FAIL ${f}`);
process.exit(failures.length ? 1 : 0);
