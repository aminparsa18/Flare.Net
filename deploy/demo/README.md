# Public demo deployment

What runs at <https://176-223-135-205.sslip.io>: Flare in read-only demo mode
([ADR-0278](../../docs-internal/adr/0278-public-read-only-demo-mode.md)) and the example shop
(`examples/ExampleApp.Shop`) sending it live data, in one Compose file behind Caddy. Sized for an
8 GB / 3 vCPU Debian VM. Only Caddy's ports 80 and 443 are published.

The sslip.io address is temporary. The demo moves to <https://demo.flarenet.live> once that
name's DNS record points at the VM.

| File | What it is |
| --- | --- |
| `docker-compose.yml` | The whole stack: Caddy, Flare (Docker Hub `edge` images), ClickHouse, Redis, Kafka, Postgres, the collector and the eight shop services |
| `Caddyfile` | TLS (Let's Encrypt) and routing: `/api/*`, `/loki/*`, `/tempo/*` to the API, everything else to the dashboard |
| `clickhouse/` | ClickHouse memory caps and unused system logs turned off |
| `.env.example` | Domain, Admin account and passwords. Copy to `.env` |
| `deploy.sh` | Redeploy: fast-forward the clone, pull images, restart what changed |

## First setup on the VM

1. As root: packages, Docker Engine with the Compose plugin (from
   [Docker's apt repo](https://docs.docker.com/engine/install/debian/), so the `docker` group
   exists before the next step), a deploy user, a firewall and swap.

   ```sh
   apt-get update && apt-get -y upgrade && apt-get install -y git curl ufw ca-certificates
   install -m 0755 -d /etc/apt/keyrings
   curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
   printf 'Types: deb\nURIs: https://download.docker.com/linux/debian\nSuites: %s\nComponents: stable\nSigned-By: /etc/apt/keyrings/docker.asc\n' \
     "$(. /etc/os-release && echo "$VERSION_CODENAME")" > /etc/apt/sources.list.d/docker.sources
   apt-get update && apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

   adduser --disabled-password --gecos "" flare && usermod -aG docker flare
   install -d -m 700 -o flare -g flare /home/flare/.ssh
   install -m 600 -o flare -g flare /dev/null /home/flare/.ssh/authorized_keys   # then add your public key

   ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443 && ufw --force enable
   fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
   echo '/swapfile none swap sw 0 0' >> /etc/fstab
   ```

   Docker's published ports bypass ufw, so the Compose file publishes nothing but Caddy.

   Once `ssh flare@<host>` works with your key, turn off password and root logins. The `00-` prefix
   matters: sshd takes the first value it reads, and Debian cloud images ship a
   `50-cloud-init.conf` that turns password login on.

   ```sh
   printf 'PasswordAuthentication no\nKbdInteractiveAuthentication no\nPermitRootLogin no\n' \
     > /etc/ssh/sshd_config.d/00-hardening.conf
   sshd -t && systemctl reload ssh
   ```

2. As `flare`: clone, then fill in `.env`. Use a non-obvious Admin username, because the login form
   is public. Generate passwords with `openssl rand -hex 24`.

   ```sh
   git clone https://github.com/aminparsa18/Flare.Net.git ~/Flare.Net
   cp ~/Flare.Net/deploy/demo/.env.example ~/Flare.Net/deploy/demo/.env
   ```

   The Admin must be set before the first start. Once the demo account exists, Flare no longer
   creates an Admin from configuration, and the API refuses to start in demo mode without one.

3. Start it: `~/Flare.Net/deploy/demo/deploy.sh`. The DNS A record must already point at the VM.
   Until it does, `DEMO_DOMAIN=<ip-with-dashes>.sslip.io` (for example `203-0-113-5.sslip.io`)
   resolves to the VM with no DNS setup and still gets a Let's Encrypt certificate. To switch to
   the real domain later, change `DEMO_DOMAIN` and run `deploy.sh` again.
   On the first start Flare.Api logs ClickHouse "connection refused" for about a minute while
   ClickHouse restarts after its init scripts. Its migration runner retries, so this is expected.

4. Sign in at `/login` as the Admin. Set **Settings > Retention** (for example 7 days), then
   create the dashboards, saved views, alert rules and SLO that visitors should see.

## Redeploy from GitHub Actions

The **Deploy demo** workflow (`.github/workflows/deploy-demo.yml`, manual) builds the shop image,
pushes it to `ghcr.io/<owner>/flare-demo-shop`, then runs `deploy.sh` over SSH. Before the first
run:

- Generate a deploy key just for this (`ssh-keygen -t ed25519 -N ""`) and append its public half
  to `flare`'s `authorized_keys`, prefixed with
  `no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty `.
- Set the `DEMO_SSH_HOST`, `DEMO_SSH_USER`, `DEMO_SSH_KEY` (the deploy key's private half) and
  `DEMO_SSH_KNOWN_HOSTS` (`ssh-keyscan -t ed25519 <host>`, without its `#` comment line) secrets,
  and create a `production` environment on the repository.
- After the first push, check that the VM can pull `flare-demo-shop` without logging in. A package
  pushed from a public repository is usually public already. If it isn't, make it public in its
  package settings.
- Flare's images come from Docker Hub's `edge` tag, so wait for **Docker Publish** to finish after
  a merge before you run it.

## Check it

```sh
curl -s https://176-223-135-205.sslip.io/api/auth/me -H 'Accept: application/json'   # "isDemo":true
curl -s -o /dev/null -w '%{http_code}\n' -X PUT https://176-223-135-205.sslip.io/api/settings/auth \
  -H 'Content-Type: application/json' -d '{"enabled":false,"localEnabled":true}'  # 403
```
