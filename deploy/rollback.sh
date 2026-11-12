#!/usr/bin/env bash
# Deploy PREVIOUS_IMAGE_TAG with deploy.sh (docs/deployment.md §9).
# Schema scripts are not undone, and the deploy/ files of the newer commit stay in place.
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"

previous="$(grep -E '^PREVIOUS_IMAGE_TAG=' .env | tail -n 1 | cut -d= -f2- || true)"
if [[ -z "$previous" ]]; then
    echo "rollback.sh: PREVIOUS_IMAGE_TAG is empty; nothing to roll back to." >&2
    exit 1
fi

echo "rollback.sh: rolling back to $previous"
exec bash ./deploy.sh "$previous"
