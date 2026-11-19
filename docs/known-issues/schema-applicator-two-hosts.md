---
title: "Known issue — two hosts run the schema applicator without a lock"
status: open
language: en
created: 2026-11-19
updated: 2026-11-19
related:
  - ../adr/0008-schema-sql-as-source-of-truth.md
  - ../architecture.md
  - ../deployment.md
---

# Known issue: two hosts run the schema applicator without a lock

**Status: open.** Seen only on a first Development start against an empty database. The VPS start order avoids it.

## Problem

`webapi` and `worker` both run `SchemaApplicator.ApplyAsync` at start-up ([ADR 0008](../adr/0008-schema-sql-as-source-of-truth.md) amendment 2026-11-19). The applicator reads `SchemaVersions`, then runs each pending script and records it. It takes no database lock.

Under the Development AppHost both hosts wait only for the database server. On an empty database both can see the same script as pending and run it at the same time. The second `CREATE` fails, and that host exits at start-up.

On the VPS, compose starts `worker` only after `webapi` is healthy ([deployment.md](../deployment.md) §8), so the `worker` finds nothing pending.

## Symptoms

| Symptom | Cause |
| --- | --- |
| On a first `dotnet run --project src/AppHost`, `webapi` or `worker` stops with `There is already an object named '…' in the database` or a duplicate key on `SchemaVersions` | Both hosts ran the same pending script |
| Starting the stopped host again works | The other host finished every script |

## Workaround

Start the failed resource again from the Aspire dashboard, or restart the AppHost.

## Possible fixes (not decided)

- Take an application lock (`sp_getapplock`) around `ApplyAsync`, so the second host waits and then finds nothing pending.
- Make the `worker` wait for `webapi` in the AppHost, as compose already does, and drop its applicator call.

Either is a code change with its own test; not part of any spec yet.
