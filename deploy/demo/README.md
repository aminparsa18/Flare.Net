# Public demo deployment

What runs at <https://demo.flarenet.live>: Flare in read-only demo mode
([ADR-0278](../../docs-internal/adr/0278-public-read-only-demo-mode.md)) and the example shop
(`examples/ExampleApp.Shop`) sending it live data, in one Compose file behind Caddy. Sized for an
8 GB / 3 vCPU Debian VM. Only Caddy's ports 80 and 443 are published.

| File | What it is |
| --- | --- |
| `docker-compose.yml` | The whole stack: Caddy, Flare (Docker Hub `edge` images), ClickHouse, Redis, Kafka, Postgres, the collector and the eight shop services |
| `Caddyfile` | TLS (Let's Encrypt) and routing: `/api/*`, `/loki/*`, `/tempo/*` to the API, everything else to the dashboard |
| `clickhouse/` | ClickHouse memory caps and unused system logs turned off |
| `.env.example` | Domain, Admin account and passwords. Copy to `.env` |
| `deploy.sh` | Redeploy: fast-forward the clone, pull images, restart what changed |

## First setup on the VM

1. As root: a deploy user in the `docker` group, a firewall and swap.

   ```sh
   adduser --disabled-password flare && usermod -aG docker flare
   apt-get install -y ufw && ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443 && ufw enable
   fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
   echo '/swapfile none swap sw 0 0' >> /etc/fstab
   ```

   Docker's published ports bypass ufw, so the Compose file publishes nothing but Caddy.

2. As `flare`: clone, then fill in `.env`. Use a non-obvious Admin username, because the login form
   is public. Generate passwords with `openssl rand -hex 24`.

   ```sh
   git clone https://github.com/aminparsa18/Flare.Net.git ~/Flare.Net
   cp ~/Flare.Net/deploy/demo/.env.example ~/Flare.Net/deploy/demo/.env
   ```

   The Admin must be set before the first start. Once the demo account exists, Flare no longer
   creates an Admin from configuration, and the API refuses to start in demo mode without one.

3. Start it: `~/Flare.Net/deploy/demo/deploy.sh`. The DNS A record must already point at the VM.

4. Sign in at `/login` as the Admin. Set **Settings > Retention** (for example 7 days), then
   create the dashboards, saved views, alert rules and SLO that visitors should see.

## Redeploy from GitHub Actions

The **Deploy demo** workflow (`.github/workflows/deploy-demo.yml`, manual) builds the shop image,
pushes it to `ghcr.io/<owner>/flare-demo-shop`, then runs `deploy.sh` over SSH. Before the first
run:

- Set the `DEMO_SSH_HOST`, `DEMO_SSH_USER`, `DEMO_SSH_KEY` and `DEMO_SSH_KNOWN_HOSTS` secrets.
- After the first push, make the `flare-demo-shop` package public, so the VM can pull it without
  logging in.
- Flare's images come from Docker Hub's `edge` tag, so wait for **Docker Publish** to finish after
  a merge before you run it.

## Check it

```sh
curl -s https://demo.flarenet.live/api/auth/me -H 'Accept: application/json'   # "isDemo":true
curl -s -o /dev/null -w '%{http_code}\n' -X PUT https://demo.flarenet.live/api/settings/auth \
  -H 'Content-Type: application/json' -d '{"enabled":false,"localEnabled":true}'  # 403
```
