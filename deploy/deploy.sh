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

# Free disk space without deleting what the next build or a rollback needs:
# - dangling layers only, so the caddy builder and base images stay cached for `build caddy`;
# - app images of any tag other than IMAGE_TAG and PREVIOUS_IMAGE_TAG, so rollback.sh starts
#   without a pull. Images in use by a container are never removed.
docker image prune -f
owner="$(read_env GHCR_OWNER)"
keep_current="$(read_env IMAGE_TAG)"
keep_previous="$(read_env PREVIOUS_IMAGE_TAG)"
docker image ls --filter "reference=ghcr.io/${owner}/mywealth-*" --format '{{.Repository}}:{{.Tag}}' |
    while read -r image; do
        image_tag="${image##*:}"
        if [[ "$image_tag" != "$keep_current" && "$image_tag" != "$keep_previous" ]]; then
            docker image rm "$image" > /dev/null || true
        fi
    done

echo "deploy.sh: $tag is healthy"
