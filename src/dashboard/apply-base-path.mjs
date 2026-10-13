// Runtime half of sub-path hosting (docs/how-to/deploy/serve-under-a-sub-path.md).
//
// SvelteKit's `paths.base` is a build-time constant - it's inlined into the server bundle,
// the client bundle and adapter-node's own static-file directory (build/client<base>/) - so
// one published image can't just read it from an env var. The image is instead built with
// a placeholder base (vite.config.ts: FLARE_BASE_PATH=/__FLARE_BASE_PATH__) and this script
// produces a rewritten copy of build/ for whatever FLARE_BASE_PATH the container starts
// with ("" = served at the root). Called from server.js on every start, from the pristine
// build/ each time, so changing the env var and restarting always works - nothing is
// rewritten in place.
import { createHash } from 'node:crypto';
import { brotliCompressSync, gzipSync } from 'node:zlib';
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

export const BASE_PATH_PLACEHOLDER = '/__FLARE_BASE_PATH__';

const REWRITE_EXTENSIONS = new Set(['.js', '.mjs', '.css', '.html', '.json', '.map']);

/** "" | "/" -> ""; "flare/" -> "/flare". Rejects anything that would not survive being spliced into a URL path. */
export function normalizeBasePath(raw) {
	const trimmed = (raw ?? '').trim().replace(/^\/+|\/+$/g, '');
	if (!trimmed) return '';
	if (!/^[A-Za-z0-9._~-]+(\/[A-Za-z0-9._~-]+)*$/.test(trimmed)) {
		throw new Error(`FLARE_BASE_PATH "${raw}" is not a valid URL path prefix (use e.g. /flare or /tools/flare).`);
	}
	return `/${trimmed}`;
}

/** True when `buildDir` was built with the placeholder base (the published image), false for a plain root-hosted build. */
export function hasPlaceholderBase(buildDir) {
	return existsSync(join(buildDir, 'client', BASE_PATH_PLACEHOLDER.slice(1)));
}

function* walk(dir) {
	for (const entry of readdirSync(dir)) {
		const full = join(dir, entry);
		if (statSync(full).isDirectory()) yield* walk(full);
		else yield full;
	}
}

/**
 * Copies `buildDir` to `outDir`, replacing the placeholder with `basePath` everywhere and
 * moving the static-file directory to match. Precompressed .gz/.br siblings of a rewritten
 * file are regenerated - sirv prefers them, so a stale copy would still carry the placeholder.
 */
export function applyBasePath(buildDir, outDir, basePath) {
	rmSync(outDir, { recursive: true, force: true });
	cpSync(buildDir, outDir, { recursive: true });

	for (const file of walk(outDir)) {
		const dot = file.lastIndexOf('.');
		if (dot < 0 || !REWRITE_EXTENSIONS.has(file.slice(dot))) continue;
		const text = readFileSync(file, 'utf8');
		if (!text.includes(BASE_PATH_PLACEHOLDER)) continue;
		const rewritten = text.replaceAll(BASE_PATH_PLACEHOLDER, basePath);
		writeFileSync(file, rewritten);
		if (existsSync(`${file}.gz`)) writeFileSync(`${file}.gz`, gzipSync(rewritten, { level: 9 }));
		if (existsSync(`${file}.br`)) writeFileSync(`${file}.br`, brotliCompressSync(rewritten));
	}

	const from = join(outDir, 'client', BASE_PATH_PLACEHOLDER.slice(1));
	const to = join(outDir, 'client', basePath);
	if (basePath) {
		mkdirSync(dirname(to), { recursive: true });
		renameSync(from, to);
	} else {
		// Root hosting: the static files live directly under client/.
		for (const entry of readdirSync(from)) renameSync(join(from, entry), join(outDir, 'client', entry));
		rmSync(from, { recursive: true });
	}

	refreshAssetTables(outDir, basePath);
}

/**
 * adapter-node 6 serves a closed set of files recorded at build time and sends the recorded
 * `size`/`etag` (plus the .gz/.br sizes) verbatim - it never re-stats the file. Rewriting the
 * placeholder changes those files' length, so without this the client gets a Content-Length
 * longer than the body (truncated JS/CSS, a broken page). The tables live in adapter-node.js as
 * `export const assets = JSON.parse("...")` (and `prerendered_assets`); recompute each entry
 * from what is now on disk. The etag is adapter-node's own scheme: sha256 of the content, base64url.
 */
function refreshAssetTables(outDir, basePath) {
	const entryFile = join(outDir, 'adapter-node.js');
	let source = readFileSync(entryFile, 'utf8');
	for (const [name, dir] of [['assets', 'client'], ['prerendered_assets', 'prerendered']]) {
		const pattern = new RegExp(`export const ${name} = JSON\\.parse\\((".*")\\);`);
		const match = pattern.exec(source);
		if (!match) throw new Error(`adapter-node.js: could not find the "${name}" asset table - adapter-node's output format changed.`);
		const table = JSON.parse(JSON.parse(match[1]));
		for (const [, entry] of table.entries) {
			const file = join(outDir, dir, basePath, entry.file);
			const content = readFileSync(file);
			entry.size = content.length;
			entry.etag = createHash('sha256').update(content).digest('base64url');
			if (entry.gz !== undefined) entry.gz = statSync(`${file}.gz`).size;
			if (entry.br !== undefined) entry.br = statSync(`${file}.br`).size;
		}
		const literal = JSON.stringify(JSON.stringify(table));
		source = source.slice(0, match.index) + `export const ${name} = JSON.parse(${literal});` + source.slice(match.index + match[0].length);
	}
	writeFileSync(entryFile, source);
}
