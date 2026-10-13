#!/usr/bin/env node
// Stages the npm packages for the Flare CLI. No dependencies; Node >= 18.
//
//   node build.mjs version
//   node build.mjs platform --rid osx-arm64 --out dist     publish + stage one platform package
//   node build.mjs main --out dist                         stage the launcher package
//
// Platform packages hold the self-contained single-file binary; the main package holds only
// launcher/flare.js and lists every platform package in optionalDependencies, pinned to the
// exact same version. See README.md in this folder.
import { spawnSync } from 'node:child_process';
import { chmodSync, copyFileSync, cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const npmRoot = resolve(here, '..');
const repoRoot = resolve(npmRoot, '..', '..');
const csproj = join(repoRoot, 'src', 'Flare.Cli', 'Flare.Cli.csproj');

// .NET RID -> npm os/cpu. Keep in sync with PLATFORM_PACKAGES in launcher/flare.js.
const TARGETS = {
  'osx-arm64': { os: 'darwin', cpu: 'arm64' },
  'osx-x64': { os: 'darwin', cpu: 'x64' },
  'linux-arm64': { os: 'linux', cpu: 'arm64' },
  'linux-x64': { os: 'linux', cpu: 'x64' },
  'win-x64': { os: 'win32', cpu: 'x64' },
};
const pkgName = (t) => `@apardev/flare-cli-${t.os}-${t.cpu}`;

function readVersion() {
  const m = /<Version>([^<]+)<\/Version>/.exec(readFileSync(csproj, 'utf8'));
  if (!m) throw new Error(`No <Version> in ${csproj}`);
  return m[1];
}

function arg(name) {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : undefined;
}

function run(cmd, args) {
  const r = spawnSync(cmd, args, { stdio: 'inherit' });
  if (r.status !== 0) throw new Error(`${cmd} ${args.join(' ')} exited ${r.status}`);
}

function stageCommon(dir, template) {
  copyFileSync(join(repoRoot, 'LICENSE'), join(dir, 'LICENSE'));
  writeFileSync(join(dir, 'package.json'), JSON.stringify(template, null, 2) + '\n');
}

const [command] = process.argv.slice(2);
const out = resolve(arg('out') ?? join(npmRoot, 'dist'));
const version = readVersion();

if (command === 'version') {
  console.log(version);
} else if (command === 'platform') {
  const rid = arg('rid');
  const target = TARGETS[rid];
  if (!target) throw new Error(`--rid must be one of ${Object.keys(TARGETS).join(', ')}`);

  const publishDir = join(out, 'publish', rid);
  const pkgDir = join(out, 'packages', pkgName(target).split('/')[1]);
  rmSync(publishDir, { recursive: true, force: true });
  rmSync(pkgDir, { recursive: true, force: true });

  // Not trimmed: Spectre.Console.Cli and the MCP SDK rely on reflection.
  run('dotnet', [
    'publish', csproj, '-c', 'Release', '-r', rid, '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', '-p:PublishTrimmed=false', '-o', publishDir,
  ]);

  const exe = target.os === 'win32' ? 'flare.exe' : 'flare';
  mkdirSync(join(pkgDir, 'bin'), { recursive: true });
  copyFileSync(join(publishDir, target.os === 'win32' ? 'Flare.Cli.exe' : 'Flare.Cli'), join(pkgDir, 'bin', exe));
  if (target.os !== 'win32') chmodSync(join(pkgDir, 'bin', exe), 0o755);

  stageCommon(pkgDir, {
    name: pkgName(target),
    version,
    description: `Flare CLI binary for ${target.os}-${target.cpu}. Install @apardev/flare-cli instead.`,
    license: 'MIT',
    homepage: 'https://github.com/aminparsa18/Flare.Net',
    repository: { type: 'git', url: 'git+https://github.com/aminparsa18/Flare.Net.git', directory: 'src/Flare.Cli.Npm' },
    os: [target.os],
    cpu: [target.cpu],
    files: ['bin/', 'LICENSE'],
    publishConfig: { access: 'public', provenance: true },
  });
  console.log(`Staged ${pkgDir}`);
} else if (command === 'main') {
  const pkgDir = join(out, 'packages', 'flare-cli');
  rmSync(pkgDir, { recursive: true, force: true });
  mkdirSync(join(pkgDir, 'bin'), { recursive: true });
  copyFileSync(join(npmRoot, 'launcher', 'flare.js'), join(pkgDir, 'bin', 'flare.js'));
  chmodSync(join(pkgDir, 'bin', 'flare.js'), 0o755);
  copyFileSync(join(npmRoot, 'README.md'), join(pkgDir, 'README.md'));

  const template = JSON.parse(readFileSync(join(npmRoot, 'package.template.json'), 'utf8'));
  template.version = version;
  template.optionalDependencies = Object.fromEntries(Object.values(TARGETS).map((t) => [pkgName(t), version]));
  stageCommon(pkgDir, template);
  console.log(`Staged ${pkgDir}`);
} else {
  console.error('usage: build.mjs version | platform --rid <rid> [--out dir] | main [--out dir]');
  process.exit(2);
}
