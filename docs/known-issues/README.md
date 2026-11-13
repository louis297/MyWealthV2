---
title: Known issues
status: draft
language: en
created: 2026-11-08
updated: 2026-11-13
---

# Known issues

Open gaps that are accepted for now. Not a backlog. A closed issue stays here with status `resolved` so the reason is not lost.

| Issue | Status | Notes |
| --- | --- | --- |
| [EODHD does not cover NZX or Tokyo](eodhd-nz-jp-coverage.md) | open | NZD / JPY seed instruments have no real EOD price. Job skips them. |
| [Deploy copy leaves stale files on the VPS](deploy-copy-leaves-stale-files.md) | open | `scp` never deletes; a file removed or renamed in `deploy/` stays on the VPS. |
| [Hosted login lockout can be triggered by anyone](hosted-login-lockout.md) | open | Five wrong passwords lock any known tenantCode + email for five minutes, smoke accounts included; unlock in SQL. |
| [Fixed Docker subnet for the compose network](deploy-fixed-docker-subnet.md) | open | `172.30.0.0/24` is pinned and repeated in `ForwardedHeaders__KnownNetworks__0` and the Caddyfile; change all of them together. |
| [Refresh grant does not re-check person or tenant status](refresh-grant-skips-status-check.md) | open | Revocation waits for the outbox; a dead-lettered event leaves a refresh token valid for up to 14 days. `webapi` still refuses its access tokens after a disable. |
| [Data Protection keys are not encrypted at rest](dataprotection-keys-unencrypted.md) | open | Key rings in `data/dataprotection/<host>/` are plain XML; readable by root or the `docker` group. |
