#!/usr/bin/env node
// Writes the environment metadata for one benchmark run: node scripts/bench-report/env.mjs <out.json> [key=value ...]
// Extra key=value pairs (suites, quick mode, ...) are recorded as-is. Called by scripts/bench.sh.
import { execSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import os from 'node:os';

const sh = (cmd) => {
	try {
		return execSync(cmd, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
	} catch {
		return null;
	}
};

const [out, ...extra] = process.argv.slice(2);
const cpu =
	sh('sysctl -n machdep.cpu.brand_string') ?? sh("grep -m1 'model name' /proc/cpuinfo | cut -d: -f2") ?? os.cpus()[0]?.model ?? 'unknown';
const osName =
	process.platform === 'darwin' ? `macOS ${sh('sw_vers -productVersion')} (${os.release()})` : (sh('. /etc/os-release && echo "$PRETTY_NAME"') ?? `${os.type()} ${os.release()}`);

const env = {
	date: new Date().toISOString(),
	cpu: cpu.trim(),
	cores: os.cpus().length,
	memoryGb: Math.round(os.totalmem() / 1024 ** 3),
	arch: os.arch(),
	os: osName,
	dotnetSdk: sh('dotnet --version'),
	node: process.versions.node,
	v8: process.versions.v8,
	gitCommit: sh('git rev-parse HEAD'),
	gitBranch: sh('git rev-parse --abbrev-ref HEAD'),
	gitDirty: (sh('git status --porcelain') ?? '').length > 0,
	...Object.fromEntries(extra.map((kv) => kv.split(/=(.*)/s).slice(0, 2)))
};

writeFileSync(out, JSON.stringify(env, null, 2) + '\n');
