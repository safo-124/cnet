# Deploying the public demo (Hetzner, Docker, Caddy)

The demo runs as two containers on one Linux server:

```
internet ──443──► Caddy (HTTPS, Let's Encrypt) ──► app :8080 (API + React app) ──► SQLite on a Docker volume
```

- `Dockerfile` builds the React app and the API into one image.
- `docker-compose.yml` runs the app, plus Caddy when started with `--profile proxy`.
- Demo mode loads the sample catalog on first start, offers the sample files for download and switches off the paid AI feature.

Tested on Ubuntu 24.04. A Hetzner CX22 (2 vCPU, 4 GB) builds and runs it comfortably.

## First deployment

### 1. Connect to the server

```bash
ssh root@<server-ip>
```

### 2. Install Docker and Git

```bash
apt update && apt install -y git
curl -fsSL https://get.docker.com | sh
```

### 3. Open only the ports that are needed

Either add a Hetzner Cloud Firewall (Console → Firewalls) allowing inbound TCP 22, 80 and 443, or use `ufw`:

```bash
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443/tcp && ufw --force enable
```

### 4. Get the code and configure the address

```bash
git clone https://github.com/safo-124/cnet.git mepcatalog
cd mepcatalog
cp deploy/.env.example .env
nano .env
```

Set `DOMAIN`. Without a domain of your own, use the server's IPv4 address with dashes and `.sslip.io`:
`203.0.113.10` → `DOMAIN=203-0-113-10.sslip.io`. That name points to your server, and Caddy can still get a real HTTPS certificate for it.

### 5. Build and start

```bash
docker compose --profile proxy up -d --build
```

The first build takes a few minutes. Then open `https://<DOMAIN>`.

## Updating after a `git push`

```bash
cd ~/mepcatalog
git pull
docker compose --profile proxy up -d --build
```

The catalog database lives on the `catalog-data` volume and survives updates.

## Useful commands

```bash
docker compose ps                        # what is running
docker compose logs -f app               # app logs (Ctrl+C to stop following)
docker compose logs caddy | grep -i cert # certificate problems
curl -s localhost:8080/healthz           # health check from the server
```

Reset the demo catalog to the sample data (deletes visitors' changes):

```bash
docker compose down
docker volume rm mepcatalog_catalog-data
docker compose --profile proxy up -d
```

## If the server already runs a web server

When nginx, Traefik or another proxy already uses ports 80 and 443, start without the bundled Caddy:

```bash
docker compose up -d --build
```

and add a site to the existing proxy that forwards to `http://127.0.0.1:8080` (or the `APP_PORT` you set in `.env`),
allowing request bodies up to 100 MB.

**Existing Caddy** (installed as a system service): add this block to `/etc/caddy/Caddyfile`, then run
`caddy validate --config /etc/caddy/Caddyfile` and `systemctl reload caddy`:

```
135-181-93-156.sslip.io {
	encode zstd gzip
	request_body {
		max_size 100MB
	}
	reverse_proxy 127.0.0.1:8080
}
```

Caddy passes `X-Forwarded-For` and `X-Forwarded-Proto` on by itself.

**nginx:** `proxy_pass http://127.0.0.1:8080;` and `client_max_body_size 100m;`, plus the usual `X-Forwarded-For` and `X-Forwarded-Proto` headers.
