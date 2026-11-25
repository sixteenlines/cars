#!/usr/bin/env bash
# Installs a Car Tracker release on this server: DB backup first, automatic rollback if it doesn't come up.
#
#   ./update.sh            install the latest GitHub release
#   ./update.sh 1.2.0      install that version (also how you go back to an older one)
#   ./update.sh --check    only show installed vs. latest version
#
# Lives in /opt/car-tracker next to .env and data/. Needs docker and curl. Run as root.
set -euo pipefail
umask 077   # .env.tmp and backups hold secrets / user data
cd "$(dirname "$(readlink -f "$0")")"

REPO="sixteenlines/cars"
IMAGE="ghcr.io/$REPO"
KEEP_BACKUPS=10
# For testing the script without GitHub: CT_LOCAL=1 uses local images and the docker-compose.yml already here.
LOCAL="${CT_LOCAL:-}"

die() { echo "✗ $*" >&2; exit 1; }
[[ -s .env ]] || die ".env is missing here ($(pwd)). See docs/DEPLOY.md."

env_get() { grep -E "^$1=" .env | tail -1 | cut -d= -f2- || true; }
env_set() {  # rewrites in place so the file keeps its permissions (chmod 600)
  { grep -vE "^$1=" .env || true; echo "$1=$2"; } > .env.tmp && cat .env.tmp > .env && rm -f .env.tmp
}

latest_release() {
  curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
    | grep -m1 '"tag_name"' | sed -E 's/.*"tag_name": *"v?([^"]+)".*/\1/'
}

# True if the running app answers /api/health with the expected version (asked from inside the container).
probe() {
  docker compose exec -T app bash -c \
    'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /api/health HTTP/1.0\r\n\r\n" >&3 && cat <&3' 2>/dev/null \
    | grep -q "\"version\":\"$1\""
}
probe_wait() { for _ in $(seq 1 45); do probe "$1" && return 0; sleep 2; done; return 1; }

current="$(env_get APP_VERSION)"
case "${1:-}" in
  --check) echo "installed: ${current:-none}"; echo "latest:    $(latest_release)"; exit 0 ;;
  "") target="$(latest_release)" || die "Couldn't ask GitHub for the latest release." ;;
  *) target="${1#v}" ;;
esac
[[ -n "$target" ]] || die "No release found."

if [[ "$target" == "$current" ]] && probe "$target"; then
  echo "✓ $target is already installed and running."; exit 0
fi
echo "→ Installing $target (installed: ${current:-none})"

# 1. Download everything first: if the version doesn't exist, nothing has been touched yet.
if [[ -z "$LOCAL" ]]; then
  docker pull -q "$IMAGE:$target" >/dev/null \
    || die "Image $IMAGE:$target not found. (After the very first release: is the package public on GitHub?)"
  curl -fsSL "https://raw.githubusercontent.com/$REPO/v$target/deploy/docker-compose.yml" -o docker-compose.yml.new \
    || die "Couldn't download docker-compose.yml for v$target."
else
  cp docker-compose.yml docker-compose.yml.new
fi

# 2. Back up the DB with the app stopped, so the copy is consistent.
backup="backups/$(date +%Y%m%d-%H%M%S)-from-${current:-none}"
if [[ -n "$current" ]]; then
  mkdir -p "$backup" && chmod 700 backups
  [[ -f docker-compose.yml ]] && cp -p docker-compose.yml "$backup/"
fi
if [[ -n "$current" && -f data/cartracker.db ]]; then
  docker compose stop app >/dev/null 2>&1 || true
  cp -a data/cartracker.db* "$backup/"
  echo "→ Database backed up to $backup"
fi

# 3. Switch version and start.
mv docker-compose.yml.new docker-compose.yml
env_set APP_VERSION "$target"
[[ -z "$LOCAL" ]] && docker compose pull -q cloudflared
docker compose up -d --remove-orphans --quiet-pull

if probe_wait "$target"; then
  echo "✓ $target is running."
  [[ -z "$LOCAL" ]] && docker image prune -f >/dev/null
  ls -1dt backups/*/ 2>/dev/null | tail -n +$((KEEP_BACKUPS + 1)) | while read -r old; do rm -rf "$old"; done || true
  exit 0
fi

# 4. Didn't come up → put everything back.
echo "✗ $target didn't come up. Last log lines:"
docker compose logs --tail 40 app || true
[[ -n "$current" ]] || die "First install failed, nothing to roll back to. Fix the problem and run again."

echo "→ Rolling back to $current"
docker compose stop app >/dev/null 2>&1 || true
if ls "$backup"/cartracker.db* >/dev/null 2>&1; then
  rm -f data/cartracker.db*
  cp -a "$backup"/cartracker.db* data/
fi
[[ -f "$backup/docker-compose.yml" ]] && cp -p "$backup/docker-compose.yml" docker-compose.yml
env_set APP_VERSION "$current"
docker compose up -d --remove-orphans --quiet-pull
if probe_wait "$current"; then
  die "Update to $target failed; back on $current with the database from before the update."
fi
die "Rollback to $current didn't come up either. Check: docker compose logs app"
