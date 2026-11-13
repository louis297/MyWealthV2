---
title: "Known issue — Data Protection keys are not encrypted at rest"
status: open
language: en
created: 2026-11-13
updated: 2026-11-13
related:
  - ../deployment.md
  - ../features/production-hosting.md
---

# Known issue: Data Protection keys are not encrypted at rest

**Status: open.** Accepted for the showcase. Check this first when a host logs a warning about an unencrypted key, or when you plan who gets access to the VPS.

## Problem

`identity`, both BFFs and `back-office` persist their ASP.NET Core Data Protection key ring to `/keys`, bind-mounted from `data/dataprotection/<host>/` on the VPS ([production-hosting.md](../features/production-hosting.md) R9, R10; [deployment.md](../deployment.md) §7). No key encryptor is configured (`src/ServiceDefaults/DataProtectionExtensions.cs`), so the key XML files hold the key material in plain text.

Anyone who can read these files can decrypt and forge the cookies of that host: the hosted-login identity cookie on `identity`, and the session cookie (which holds the access and refresh tokens) on the BFFs and `back-office`.

Reading them needs root or `docker` group access on the VPS. Either already gives full control of every host, the `.env` secrets and the database, so encryption at rest would not stop that person.

## Symptoms

| Symptom | Cause |
| --- | --- |
| On start, a host logs `No XML encryptor configured. Key ... may be persisted to storage in unencrypted form.` | Expected. This is the gap described here, not a fault |

## How to check

On the VPS:

```bash
ls -l /opt/mywealth/data/dataprotection/*/
```

Each folder belongs to UID 1654 and holds `key-*.xml` files. No other user than the deploy user and root should be able to log in to the VPS.

## How to fix

If the key files may have leaked, stop the four public hosts, delete the `key-*.xml` files in every `data/dataprotection/<host>/` folder, and start the hosts again. They create a new key ring. Everyone signs in again.

## What we are not doing now

- Encrypting the keys with a certificate (`ProtectKeysWithCertificate`). The certificate and its password would sit on the same VPS, next to the keys.
- Storing the keys in an external key vault. This deploy has no cloud dependency.

## Revisit when

- Someone other than the owner gets shell or `docker` access to the VPS.
- The keys move off the VPS, for example to shared storage or a second instance.
