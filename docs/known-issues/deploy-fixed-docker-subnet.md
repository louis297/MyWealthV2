---
title: "Known issue — fixed Docker subnet for the compose network"
status: open
language: en
created: 2026-11-12
updated: 2026-11-13
related:
  - ../deployment.md
  - ../features/production-hosting.md
---

# Known issue: fixed Docker subnet for the compose network

**Status: open.** Works on a fresh VPS. Check this first when Docker refuses to create the network, or when a host stops trusting the forwarded scheme.

## Problem

`deploy/compose.yml` pins the `internal` network to `172.30.0.0/24`. The public hosts (`identity`, both BFFs, `back-office`) trust `X-Forwarded-Proto` and `X-Forwarded-For` only from the networks in `ForwardedHeaders__KnownNetworks__0`, which is the same literal `172.30.0.0/24` ([production-hosting.md](../features/production-hosting.md)). The subnet is pinned so that value is known in advance.

The Caddy global rate limit skips the same range, and keeps its gateway `172.30.0.1` limited (`deploy/caddy/Caddyfile`, [deployment.md](../deployment.md) §11).

The same range therefore lives in two files, and it must not overlap any other network on the VPS.

## Symptoms

| Symptom | Cause |
| --- | --- |
| `docker compose up` fails with `Pool overlaps with other one on this address space` | Another Docker network, a VPN (WireGuard, Tailscale), or a provider private network already uses `172.30.0.0/24` or a range that contains it |
| Browser loops between `http` and `https` on `id.<domain>`, or OIDC redirect URIs come out as `http://` | The subnet was changed in `networks:` but not in every `ForwardedHeaders__KnownNetworks__0`, so the hosts ignore the headers from Caddy |
| Portal sign-in or refresh fails with 429 under load | The subnet was changed in `compose.yml` but not in the Caddyfile, so host-to-host calls hit the per-IP global limit |

## How to check

On the VPS:

```bash
docker network inspect mywealth_internal --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}'
docker network ls -q | xargs docker network inspect --format '{{.Name}} {{range .IPAM.Config}}{{.Subnet}} {{end}}'
ip -4 route
```

The first line must print `172.30.0.0/24`. No other network or route may overlap it.

## How to fix

Pick a free private range (for example `172.31.0.0/24`) and change **every** occurrence in one commit: in `deploy/compose.yml` the `networks.internal.ipam` subnet and each `ForwardedHeaders__KnownNetworks__0`; in `deploy/caddy/Caddyfile` the global-limit expression, including the gateway address (`.1` of the new range). Then deploy. `docker compose up` recreates the network.

```bash
grep -rn '172\.30\.0\.' deploy/   # every hit must change together
```

## What we are not doing now

- Deriving the trusted range from the container's own network at start-up. It hides which proxy is trusted.
- Moving the range into `.env`. It would be one more per-server value for a problem that has not happened.

## Revisit when

- The VPS gains a VPN or private network in `172.16.0.0/12`.
- A second compose project runs on the same VPS.
