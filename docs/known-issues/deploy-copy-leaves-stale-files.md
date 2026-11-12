---
title: "Known issue — deploy copy leaves stale files on the VPS"
status: open
language: en
created: 2026-11-12
updated: 2026-11-12
related:
  - ../deployment.md
---

# Known issue: deploy copy leaves stale files on the VPS

**Status: open.** Harmless today. Check this first when the VPS behaves as if an old file were still in use.

## Problem

The deploy job copies the `deploy/` folder with `scp -r deploy/. <vps>:/opt/mywealth/` ([deployment.md](../deployment.md) §9 step 3.2). `scp` adds and overwrites files. It never deletes. A file removed or renamed in `deploy/` stays on the VPS under its old name.

## When it bites

| Change in `deploy/` | What stays on the VPS | Effect |
| --- | --- | --- |
| A file is deleted | The old file | Usually none. A file that compose or a script still reads by name keeps working from the stale copy, so the deletion looks fine until the VPS is rebuilt |
| A file is renamed (for example `compose.yml` → `docker-compose.yml`) | Both names | Docker Compose prefers `compose.yaml` / `compose.yml` over `docker-compose.yml`, so the old file may win |
| A file is moved into or out of `caddy/` | Both copies | The Caddy container mounts the whole `caddy/` folder; a stale Caddyfile import or snippet can still be read |

`.env`, `certs/` and `data/` are not in `deploy/`, so the copy never touches them. That part is intended.

## How to check

On the VPS, as `deploy`, compare the folder with the repository:

```bash
cd /opt/mywealth
find . -path ./data -prune -o -path ./certs -prune -o -type f -print | sort
```

Every file listed, except `.env`, should exist in `deploy/` at the deployed commit (`IMAGE_TAG` in `.env`). Delete any other file by hand.

## What we are not doing now

- Switching the copy to `rsync --delete`. It needs `rsync` on both ends, and a wrong target path deletes `.env`, `certs/` and `data/`. That risk is larger than the stale-file risk while `deploy/` changes rarely.
- Deleting the target folder before each copy, for the same reason.

## Revisit when

- A file in `deploy/` is renamed or removed. Delete the old file on the VPS in the same change, or switch to `rsync --delete --exclude .env --exclude certs/ --exclude data/`.
- A deploy behaves differently from a fresh rebuild of the VPS.
