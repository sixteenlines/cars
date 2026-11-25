# Deployment

```
Dev machine: git tag v1.2.0 && git push origin v1.2.0
        │
GitHub Actions: tests → Docker image ghcr.io/sixteenlines/cars:1.2.0 → GitHub release
        │
Server: ./update.sh → DB backup → new image → health check → (rollback if it fails)
        │
cloudflared (outbound tunnel) ⇄ Cloudflare ⇄ https://cars.example.com
```

No ports are opened on the router, the server never builds anything and holds no source code — only:

```
/opt/car-tracker/
  .env                  secrets + installed version (chmod 600)
  docker-compose.yml    replaced by update.sh with the one from the installed release
  update.sh
  data/                 SQLite DB, photos, bills, cookie keys  ← the thing that matters
  backups/              DB copies taken before each update (last 10)
```

---

## 1. One-time server setup

Any small Linux machine or VM/container with Docker works (1 GB RAM, a few GB disk plus your photos).
Nothing needs to be reachable from the internet.

### 1.1 Prepare the host (Debian/Ubuntu)

```bash
apt update && apt full-upgrade -y
apt install -y curl ca-certificates unattended-upgrades
dpkg-reconfigure -plow unattended-upgrades     # automatic security updates
curl -fsSL https://get.docker.com | sh
docker run --rm hello-world
mkdir -p /opt/car-tracker/data && chown 1654:1654 /opt/car-tracker/data   # app runs as uid 1654
```

Recommended: SSH with keys only (`PasswordAuthentication no`), SSH reachable from the LAN only.

**Docker in an unprivileged Proxmox LXC** needs the container features `nesting=1,keyctl=1`. If
`hello-world` fails with `open sysctl net.ipv4.ip_unprivileged_port_start … permission denied`, that's a known
clash between newer containerd/runc and LXC's AppArmor setup (Proxmox bug 7006): update the Proxmox host and
restart the container. Don't work around it with `lxc.apparmor.profile: unconfined`.

### 1.2 SSH from your computer
`~/.ssh/config`:
```
Host cars
  HostName <server IP>
  User root
```

### 1.3 Cloudflare tunnel
Zero Trust → Networks → Tunnels → *Create a tunnel* → Cloudflared → `car-tracker`. Copy only the token
(the string after `--token`, don't run the command). Published application / public hostname:
your subdomain + domain (e.g. `cars` · `example.com`), service URL **`http://app:8080`** (`app` = the compose service; plain HTTP,
Cloudflare does HTTPS). The tunnel shows *Inactive* until the stack runs.

### 1.4 Server files

```bash
ssh cars
cd /opt/car-tracker
curl -fsSLO https://raw.githubusercontent.com/sixteenlines/cars/main/deploy/update.sh && chmod 700 update.sh
nano .env && chmod 600 .env
```

`.env` (template: [`deploy/.env.example`](../deploy/.env.example)):
```
PUBLIC_URL=https://cars.example.com
CLOUDFLARE_TUNNEL_TOKEN=<token>
OPENAI_API_KEY=<server key, own OpenAI project with a budget limit>
APP_VERSION=
```

---

## 2. Releasing

1. Everything that should ship is merged into `main`, CI is green.
2. Tag and push:
   ```bash
   git tag v1.2.0 && git push origin v1.2.0
   ```
3. GitHub → Actions → *Release* takes a few minutes. Result: image `ghcr.io/sixteenlines/cars:1.2.0`
   and a GitHub release with generated notes.

Versions: `MAJOR.MINOR.PATCH`. Bug fix → patch, new feature → minor.

**Very first release only:** GitHub → your profile → Packages → `cars` → Package settings → check that
visibility is **Public** (the server pulls without login). If `update.sh` says "image not found", this is it.

## 3. Installing a release (on the server)

```bash
ssh cars /opt/car-tracker/update.sh           # latest release
ssh cars /opt/car-tracker/update.sh 1.1.0     # a specific version
ssh cars /opt/car-tracker/update.sh --check   # installed vs. latest
```

What it does: downloads the image and that release's `docker-compose.yml` first (nothing changes if that
fails) → stops the app and copies the DB to `backups/` → starts the new version (DB migrations run on start)
→ waits until `/api/health` reports the new version. If it doesn't come up within ~90 s: shows the log,
restores the DB copy and the previous version.

**First install:** afterwards, get the one-time admin invite (valid 24 h):
```bash
ssh cars 'cd /opt/car-tracker && docker compose logs app | grep register'
```

**Going back on purpose** (`update.sh <older version>`): fine for releases without DB changes. If the newer
release changed the DB schema, also restore the DB from the backup taken when you updated:
`docker compose stop app && cp -a backups/<folder>/cartracker.db* data/ && docker compose up -d`.

### Optional: semi-automatic updates
Once updates have been boring for a while, let the server install new releases at night — publishing a
release then *is* the deploy decision:

```bash
cat > /etc/systemd/system/car-tracker-update.service <<'EOF'
[Unit]
Description=Install latest Car Tracker release
[Service]
Type=oneshot
ExecStart=/opt/car-tracker/update.sh
EOF
cat > /etc/systemd/system/car-tracker-update.timer <<'EOF'
[Unit]
Description=Nightly Car Tracker update
[Timer]
OnCalendar=*-*-* 04:00
RandomizedDelaySec=30m
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload && systemctl enable --now car-tracker-update.timer
journalctl -u car-tracker-update   # what happened
```

---

## 4. Operations

**Logs**
```bash
ssh cars 'cd /opt/car-tracker && docker compose ps'
ssh cars 'cd /opt/car-tracker && docker compose logs --tail 100 app'
ssh cars 'cd /opt/car-tracker && docker compose logs --tail 50 cloudflared'
```

**Backups.** Everything that matters is `/opt/car-tracker/data` (plus `.env`). Back it up to a different
machine — e.g. the hypervisor's backup jobs or a nightly copy. For a consistent file copy stop the app first
(`docker compose stop app`); `update.sh` keeps DB copies of the last updates in `backups/`.

**Good practice:** nothing listens on the internet (cloudflared connects out), SSH from
the LAN with keys only, automatic Debian security updates, one machine/container per job.
