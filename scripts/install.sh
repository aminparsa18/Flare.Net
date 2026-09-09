#!/usr/bin/env bash
# Zero-prerequisite quick-install for Flare's standalone (docker-compose) stack -
# docs-internal/planning/roadmap.md's "fourth, even-lower-friction entry point
# alongside the existing three install paths (Aspire integration, `docker compose up`,
# the `flare` CLI)". Meant for an evaluator with neither this repo cloned nor a .NET
# SDK installed:
#
#   curl -fsSL https://raw.githubusercontent.com/aminparsa18/Flare.Net/main/scripts/install.sh | bash
#
# What it does, in order:
#   1. Detects Docker (and the Compose v2 plugin); installs Docker if missing.
#   2. Fetches just what's needed to run the stack - docker-compose.install.yml (the
#      image-based counterpart to the repo's own docker-compose.yml, see that file's
#      header comment), .env.example, and db/clickhouse/*.sql - into an install
#      directory. NOT a full `git clone`: nothing here needs to build from source,
#      since ingest/api/dashboard run from the images docker-publish.yml already
#      published to Docker Hub.
#   3. `docker compose pull && docker compose up -d`, waits for `api` to report
#      healthy, then prints the dashboard URL.
#
# Safe to re-run: it re-pulls the compose file/migrations and the latest images but
# never touches an existing .env or the named volumes (clickhouse-data/redis-data/
# identity-data), so your data and any port/credential overrides survive.
#
# Env vars you can override (none are required):
#   FLARE_INSTALL_DIR   Where to put the stack files. Default: $HOME/.flare
#   FLARE_INSTALL_REF    git ref (branch or tag) to fetch files from. Default: main
#   FLARE_IMAGE_TAG      Docker Hub tag for the three Flare images. Default: latest
set -euo pipefail

FLARE_INSTALL_DIR="${FLARE_INSTALL_DIR:-$HOME/.flare}"
FLARE_INSTALL_REF="${FLARE_INSTALL_REF:-main}"
FLARE_IMAGE_TAG="${FLARE_IMAGE_TAG:-latest}"
REPO="aminparsa18/Flare.Net"

log()  { printf '==> %s\n' "$1"; }
warn() { printf 'WARNING: %s\n' "$1" >&2; }
die()  { printf 'ERROR: %s\n' "$1" >&2; exit 1; }

# Runs docker/docker compose with sudo only if the current user can't already talk to
# the daemon (e.g. just-installed Docker on Linux, before a fresh login picks up the
# new `docker` group membership) - avoids forcing sudo on the common case (Docker
# Desktop on macOS, or a Linux box where the user is already in the docker group).
DOCKER=(docker)
maybe_use_sudo_for_docker() {
	if docker info >/dev/null 2>&1; then
		return
	fi
	if [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then
		warn "Current user can't reach the Docker daemon yet (group membership needs a fresh login) - using sudo for this run."
		DOCKER=(sudo docker)
	fi
}

# ---- 1. Docker detection / install -----------------------------------------------

have_docker_and_compose() {
	command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1
}

install_docker_linux() {
	log "Docker not found - installing via Docker's official convenience script (get.docker.com)."
	# get.docker.com detects the distro (apt/dnf/yum/zypper) itself and installs the
	# compose-plugin alongside the engine on every distro it supports - reimplementing
	# that per-package-manager detection here would just duplicate a script Docker
	# already maintains. It refuses unsupported distros with a clear message rather
	# than silently doing the wrong thing, which is exactly the failure mode we want.
	local script
	script="$(mktemp)"
	curl -fsSL https://get.docker.com -o "$script"
	if [ "$(id -u)" -eq 0 ]; then
		sh "$script"
	elif command -v sudo >/dev/null 2>&1; then
		sudo sh "$script"
	else
		die "Docker install needs root and 'sudo' isn't available - re-run as root, or install Docker yourself: https://docs.docker.com/engine/install/"
	fi
	rm -f "$script"

	# Best-effort: start the daemon now and let future logins pick it up automatically,
	# and add the invoking user to the docker group so they don't need sudo next time
	# (takes effect on their next login, not this one - see maybe_use_sudo_for_docker).
	if command -v systemctl >/dev/null 2>&1; then
		sudo systemctl enable --now docker >/dev/null 2>&1 || true
	fi
	if [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null 2>&1; then
		sudo usermod -aG docker "$(id -un)" >/dev/null 2>&1 || true
		warn "Added $(id -un) to the 'docker' group - log out and back in for that to take effect. Using sudo for the rest of this run."
	fi
}

install_docker_macos() {
	if command -v brew >/dev/null 2>&1; then
		log "Docker not found - installing Docker Desktop via Homebrew (brew install --cask docker)."
		brew install --cask docker
		log "Starting Docker Desktop..."
		open -a Docker || true
	else
		die "Docker not found and Homebrew isn't installed, so this script can't install Docker Desktop unattended. Install it from https://www.docker.com/products/docker-desktop/, start it, then re-run this script."
	fi
}

install_docker() {
	case "$(uname -s)" in
	Linux)
		# WSL has no systemd/daemon of its own to manage in the common case - Docker
		# Desktop for Windows (WSL2 backend) is the supported path there, and it can't
		# be installed from inside a WSL shell.
		if grep -qi microsoft /proc/version 2>/dev/null; then
			die "Detected WSL. Install Docker Desktop for Windows (WSL2 backend) from https://www.docker.com/products/docker-desktop/, enable it for this distro, then re-run this script."
		fi
		install_docker_linux
		;;
	Darwin)
		install_docker_macos
		;;
	*)
		die "Unsupported platform '$(uname -s)'. Install Docker yourself: https://docs.docker.com/get-docker/"
		;;
	esac
}

wait_for_docker_daemon() {
	log "Waiting for the Docker daemon to be ready..."
	for _ in $(seq 1 60); do
		if "${DOCKER[@]}" info >/dev/null 2>&1; then
			return
		fi
		sleep 2
	done
	die "Docker daemon never became ready. On macOS, make sure Docker Desktop finished starting (check the menu-bar icon), then re-run this script."
}

if ! have_docker_and_compose; then
	install_docker
fi
maybe_use_sudo_for_docker
wait_for_docker_daemon
if ! "${DOCKER[@]}" compose version >/dev/null 2>&1; then
	die "Docker was found but the Compose v2 plugin wasn't. Install it: https://docs.docker.com/compose/install/"
fi
log "Docker is ready: $("${DOCKER[@]}" --version)"

# ---- 2. Fetch the stack files (no git clone needed) --------------------------------

log "Setting up $FLARE_INSTALL_DIR"
mkdir -p "$FLARE_INSTALL_DIR"

# A GitHub codeload tarball, not `git clone` - the whole point of this script is
# working without git or a .NET SDK. Extracted to a scratch dir first so only the
# handful of paths the stack actually needs (compose file, .env.example, ClickHouse
# migrations) get copied into $FLARE_INSTALL_DIR, not the whole source tree.
tmp_src="$(mktemp -d)"
trap 'rm -rf "$tmp_src"' EXIT

log "Fetching stack files from $REPO@$FLARE_INSTALL_REF"
curl -fsSL "https://codeload.github.com/$REPO/tar.gz/$FLARE_INSTALL_REF" \
	| tar -xz -C "$tmp_src"
extracted_root="$(find "$tmp_src" -mindepth 1 -maxdepth 1 -type d | head -n1)"
[ -n "$extracted_root" ] || die "Could not find '$FLARE_INSTALL_REF' in $REPO - check FLARE_INSTALL_REF."

cp "$extracted_root/docker-compose.install.yml" "$FLARE_INSTALL_DIR/docker-compose.yml"
rm -rf "$FLARE_INSTALL_DIR/db"
mkdir -p "$FLARE_INSTALL_DIR/db"
cp -R "$extracted_root/db/clickhouse" "$FLARE_INSTALL_DIR/db/clickhouse"

# Never overwrite an existing .env - it may carry a real evaluator's port/credential
# overrides from a previous run of this same script.
if [ ! -f "$FLARE_INSTALL_DIR/.env" ]; then
	cp "$extracted_root/.env.example" "$FLARE_INSTALL_DIR/.env"
fi

# ---- 3. Bring the stack up -----------------------------------------------------

cd "$FLARE_INSTALL_DIR"
log "Pulling images (tag: $FLARE_IMAGE_TAG)"
DOCKERHUB_USERNAME="${DOCKERHUB_USERNAME:-xracer007}" FLARE_IMAGE_TAG="$FLARE_IMAGE_TAG" \
	"${DOCKER[@]}" compose pull

log "Starting the stack (docker compose up -d)"
DOCKERHUB_USERNAME="${DOCKERHUB_USERNAME:-xracer007}" FLARE_IMAGE_TAG="$FLARE_IMAGE_TAG" \
	"${DOCKER[@]}" compose up -d

log "Waiting for the api service to report healthy..."
for i in $(seq 1 60); do
	status="$("${DOCKER[@]}" compose ps --format '{{.Health}}' api 2>/dev/null || true)"
	if [ "$status" = "healthy" ]; then
		break
	fi
	if [ "$i" -eq 60 ]; then
		warn "api did not report healthy in time - check '${DOCKER[*]} compose logs api' in $FLARE_INSTALL_DIR."
	fi
	sleep 2
done

dashboard_port="$(grep -E '^FLARE_DASHBOARD_PORT=' "$FLARE_INSTALL_DIR/.env" 2>/dev/null | cut -d= -f2)"
dashboard_port="${dashboard_port:-7777}"

cat <<EOF

==> Flare is up: http://localhost:$dashboard_port

Stack files: $FLARE_INSTALL_DIR
  Stop:      cd $FLARE_INSTALL_DIR && docker compose stop
  Start:     cd $FLARE_INSTALL_DIR && docker compose start
  Logs:      cd $FLARE_INSTALL_DIR && docker compose logs -f
  Uninstall: cd $FLARE_INSTALL_DIR && docker compose down -v && cd .. && rm -rf $FLARE_INSTALL_DIR

Point an OTLP logger at it - gRPC :4317, HTTP :4318. See:
  https://github.com/$REPO/blob/main/docs/how-to/run-standalone.md
EOF
