#!/usr/bin/env bash
# Deploy one image tag and wait until every container is healthy (docs/deployment.md §9).
# Usage: deploy.sh <image-tag>
# Runs on the VPS in /opt/mywealth as the deploy user.
set -euo pipefail

tag="${1:?usage: deploy.sh <image-tag>}"
cd "$(dirname "$(readlink -f "$0")")"

if [[ ! -f .env ]]; then
    echo "deploy.sh: .env is missing in $PWD. Create it from .env.example first." >&2
    exit 1
fi

read_env() {
    grep -E "^$1=" .env | tail -n 1 | cut -d= -f2- || true
}

write_env() {
    local key="$1" value="$2"
    if grep -qE "^$key=" .env; then
        sed -i "s|^$key=.*|$key=$value|" .env
    else
        printf '%s=%s\n' "$key" "$value" >> .env
    fi
}

current="$(read_env IMAGE_TAG)"
if [[ "$current" != "$tag" ]]; then
    write_env PREVIOUS_IMAGE_TAG "$current"
fi
write_env IMAGE_TAG "$tag"
echo "deploy.sh: deploying $tag (previous: ${current:-none})"

# A changed caddy/Dockerfile takes effect.
docker compose build caddy

docker compose pull --ignore-buildable

# Returns only when every container with a health check is healthy; fails otherwise.
docker compose up -d --remove-orphans --wait --wait-timeout 600

# A changed Caddyfile takes effect even when the caddy container was not recreated.
docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile

# rollback.sh pulls the previous tag again, so unused images can go.
docker image prune -af

echo "deploy.sh: $tag is healthy"
