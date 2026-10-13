#!/usr/bin/env node
// Launcher for the self-contained `flare` binary shipped in a per-platform npm package
// (@apardev/flare-cli-<os>-<arch>, pulled in through optionalDependencies so npm only
// downloads the one matching this machine). The idea - a thin Node shim over a
// self-contained .NET executable - is from https://github.com/adrianstevens/npm-cli-wrapper;
// the per-platform-package layout is the esbuild/swc one.
'use strict';

const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const PLATFORM_PACKAGES = {
  'darwin-arm64': '@apardev/flare-cli-darwin-arm64',
  'darwin-x64': '@apardev/flare-cli-darwin-x64',
  'linux-arm64': '@apardev/flare-cli-linux-arm64',
  'linux-x64': '@apardev/flare-cli-linux-x64',
  'win32-x64': '@apardev/flare-cli-win32-x64',
};

function fail(message) {
  console.error(`flare: ${message}`);
  process.exit(1);
}

function resolveBinary() {
  // Escape hatch for local testing / unsupported layouts.
  if (process.env.FLARE_CLI_BINARY_PATH) {
    return process.env.FLARE_CLI_BINARY_PATH;
  }

  const key = `${process.platform}-${process.arch}`;
  const pkg = PLATFORM_PACKAGES[key];
  if (!pkg) {
    fail(
      `unsupported platform ${key}. Supported: ${Object.keys(PLATFORM_PACKAGES).join(', ')}. ` +
        'Install the .NET tool instead: dotnet tool install --global Flare.Cli');
  }

  let pkgJson;
  try {
    pkgJson = require.resolve(`${pkg}/package.json`);
  } catch {
    fail(
      `the platform package ${pkg} is not installed. This happens when npm skipped optional ` +
        'dependencies (--omit=optional) or the install was done on a different OS/CPU. ' +
        'Reinstall with: npm install --global @apardev/flare-cli');
  }

  return path.join(path.dirname(pkgJson), 'bin', process.platform === 'win32' ? 'flare.exe' : 'flare');
}

const binary = resolveBinary();

if (!fs.existsSync(binary)) {
  fail(`binary not found at ${binary}`);
}

if (process.platform !== 'win32') {
  // Tarballs normally keep the exec bit; this covers installs that lost it.
  try {
    fs.accessSync(binary, fs.constants.X_OK);
  } catch {
    try {
      fs.chmodSync(binary, 0o755);
    } catch (err) {
      fail(`${binary} is not executable and chmod failed: ${err.message}`);
    }
  }
}

let child;
try {
  child = spawn(binary, process.argv.slice(2), { stdio: 'inherit' });
} catch (err) {
  // spawn can throw synchronously (e.g. EBADARCH: an x64 Node cannot exec an arm64 binary).
  fail(`could not start ${binary}: ${err.message}`);
}

// Ctrl+C reaches the child directly (same process group); forward the signals a
// terminal does not deliver itself (e.g. `kill`, service managers).
for (const signal of ['SIGTERM', 'SIGHUP']) {
  process.on(signal, () => child.kill(signal));
}

child.on('error', (err) => fail(`could not start ${binary}: ${err.message}`));

child.on('exit', (code, signal) => {
  if (signal) {
    // Re-raise so the caller sees "killed by signal", not a silent exit 0.
    process.removeAllListeners(signal);
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 1);
});
