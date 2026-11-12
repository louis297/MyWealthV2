---
title: Known issues
status: draft
language: en
created: 2026-11-08
updated: 2026-11-12
---

# Known issues

Open gaps that are accepted for now. Not a backlog. A closed issue stays here with status `resolved` so the reason is not lost.

| Issue | Status | Notes |
| --- | --- | --- |
| [EODHD does not cover NZX or Tokyo](eodhd-nz-jp-coverage.md) | open | NZD / JPY seed instruments have no real EOD price. Job skips them. |
| [Deploy copy leaves stale files on the VPS](deploy-copy-leaves-stale-files.md) | open | `scp` never deletes; a file removed or renamed in `deploy/` stays on the VPS. |
| [Fixed Docker subnet for the compose network](deploy-fixed-docker-subnet.md) | open | `172.30.0.0/24` is pinned and repeated in `ForwardedHeaders__KnownNetworks__0`; change all of them together. |
