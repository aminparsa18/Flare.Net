// Pure helpers behind the /errors stack-trace "open in repo" links (ADR-0095): pull a
// `path:line` out of a stack frame's location, work out which commit the app was built from,
// and build the host-specific "file at commit, line N" URL. No I/O and no Svelte - kept
// separate from StackTraceViewer so the URL rules sit in one place.

export type SourceLinkProvider = 'GitHub' | 'GitLab' | 'AzureDevOps';

/** Mirrors `SourceLinkDto` (Flare.Api/Model/SourceLinkModels.cs). */
export interface SourceLinkConfig {
	serviceName: string;
	provider: SourceLinkProvider;
	repoUrl: string;
	defaultRef: string;
	pathPrefix: string;
}

export interface FrameLocation {
	path: string;
	line: number;
}

/**
 * `path:line` from a frame's location text - .NET's `/src/File.cs:line 228` or the
 * Node/Java-style `/src/file.js:10:5` / `File.java:42`. Null when it isn't a path:line pair.
 */
export function parseFrameLocation(location: string): FrameLocation | null {
	const text = location.trim();
	const m = /^(.+?):line\s+(\d+)$/.exec(text) ?? /^(.+?):(\d+)(?::\d+)?$/.exec(text);
	if (!m) return null;
	const line = Number(m[2]);
	return line > 0 ? { path: m[1], line } : null;
}

export interface ResolvedRef {
	ref: string;
	/** True for a commit SHA, false for a configured branch/tag - Azure DevOps needs to know. */
	isCommit: boolean;
}

/**
 * The commit to link to. `revision` is the occurrence's `vcs.revision` / `service.version`:
 * a bare SHA, or a SourceLink-style `1.2.3+<sha>` informational version. Anything else (a plain
 * `1.2.3`, which names no commit) falls back to the service's default branch/tag, or null.
 */
export function resolveRef(revision: string, config: SourceLinkConfig): ResolvedRef | null {
	const candidate = revision.includes('+') ? revision.slice(revision.lastIndexOf('+') + 1) : revision;
	if (/^[0-9a-f]{7,40}$/i.test(candidate)) return { ref: candidate, isCommit: true };
	return config.defaultRef ? { ref: config.defaultRef, isCommit: false } : null;
}

/**
 * Repo-relative path for a frame's build-machine path. Strips the configured prefix (and the
 * `/_/` root deterministic builds rewrite paths to). A relative path with a directory is used
 * as-is; an absolute path no prefix explains, or a bare file name, returns null - no link beats
 * a wrong one.
 */
export function repoRelativePath(path: string, config: SourceLinkConfig): string | null {
	const normalized = path.replaceAll('\\', '/');
	const prefix = config.pathPrefix.replaceAll('\\', '/');
	if (prefix && normalized.toLowerCase().startsWith(prefix.toLowerCase())) {
		return normalized.slice(prefix.length).replace(/^\/+/, '') || null;
	}
	if (normalized.startsWith('/_/')) return normalized.slice(3);
	if (normalized.startsWith('/') || /^[a-z]:\//i.test(normalized)) return null;
	return normalized.includes('/') ? normalized.replace(/^\.\//, '') : null;
}

function encodePath(path: string): string {
	return path.split('/').map(encodeURIComponent).join('/');
}

/** The host-specific URL for `path` at `ref`, line `line`. */
export function buildSourceUrl(config: SourceLinkConfig, ref: ResolvedRef, path: string, line: number): string {
	const repo = config.repoUrl.replace(/\/+$/, '');
	switch (config.provider) {
		case 'GitLab':
			return `${repo}/-/blob/${encodePath(ref.ref)}/${encodePath(path)}#L${line}`;
		case 'AzureDevOps': {
			const version = `${ref.isCommit ? 'GC' : 'GB'}${ref.ref}`;
			const query = new URLSearchParams({
				path: `/${path}`,
				version,
				line: String(line),
				lineEnd: String(line + 1),
				lineStartColumn: '1',
				lineEndColumn: '1'
			});
			return `${repo}?${query}`;
		}
		default:
			return `${repo}/blob/${encodePath(ref.ref)}/${encodePath(path)}#L${line}`;
	}
}

/** Builds the `linkFor` callback StackTraceViewer takes, or undefined when there's nothing to link to. */
export function createFrameLinker(config: SourceLinkConfig | undefined, revision: string): ((location: string) => string | null) | undefined {
	if (!config) return undefined;
	const ref = resolveRef(revision, config);
	if (!ref) return undefined;
	return (location) => {
		const frame = parseFrameLocation(location);
		if (!frame) return null;
		const path = repoRelativePath(frame.path, config);
		return path ? buildSourceUrl(config, ref, path, frame.line) : null;
	};
}
