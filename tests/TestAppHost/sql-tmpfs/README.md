# Test SQL Server on tmpfs (local only)

An opt-in for a developer machine. The test SQL Server keeps all of its files (system databases, test
databases, logs) on tmpfs, in the memory of the Docker VM, so a test run does not write its databases to
the SSD. CI never uses it: `TestAppHost` reads the switch only outside CI.

Measured on 2026-11-25 (36 cores, Docker Desktop with 32 GiB):

| Suite | On disk | On tmpfs |
| --- | --- | --- |
| `Infrastructure.IntegrationTests` | 3m26s, 2.8 GB written to the SSD | 2m17s, 0.17 GB |
| `Application.FunctionalTests` | 6m27s, 4.2 GB written to the SSD | 5m55s, 0.38 GB |

The tmpfs held about 4.5 GB after both suites. It is capped at 16 GB.

## Why an image of its own

SQL Server opens its data and log files with `O_DIRECT`, and tmpfs refuses `O_DIRECT`, so the stock image
does not start on tmpfs. `nodirect.c` is a small `LD_PRELOAD` library that removes `O_DIRECT` from the
open calls. The `Dockerfile` adds it to the same `mcr.microsoft.com/mssql/server:2022-latest` image that
the test host uses otherwise. This is not a setup Microsoft supports. It is fine for throwaway test data
and nothing else.

## Set up

1. Give the Docker VM enough memory: Docker Desktop, Settings, Resources, Advanced, Memory limit. 32 GB
   works; the tmpfs and the SQL Server buffer pool both come out of it.
2. Build the image (again after a SQL Server image update you want to pick up):

   ```bash
   docker pull mcr.microsoft.com/mssql/server:2022-latest
   docker build -t mywealth-mssql-tmpfs:local tests/TestAppHost/sql-tmpfs
   ```

3. Turn it on for every shell, so test runs from the terminal, the IDE and agents all see it. Add this
   line to `~/.profile` and log in again (or `source ~/.profile` in open shells):

   ```bash
   export MYWEALTH_TEST_SQL_TMPFS_IMAGE=mywealth-mssql-tmpfs:local
   ```

4. Run any database-backed test project. The test host sees the changed container settings and replaces
   the persistent `dbserver-test-*` container with one on tmpfs.

Check it:

```bash
docker inspect $(docker ps -q -f name=dbserver-test) --format '{{.Config.Image}} {{.HostConfig.Tmpfs}}'
docker stats --no-stream $(docker ps -q -f name=dbserver-test)   # BLOCK I/O stays near 0B
```

## Turn it off

Remove the line from `~/.profile`, open a new shell and run a test project. The test host goes back to
the stock image on disk.

## What to expect

- When the container stops (a Docker restart, a reboot), everything in it goes. SQL Server sets up its
  system databases again when it starts, and each test project recreates its own databases anyway, so
  the next run only pays the SQL Server start-up.
- If the test host fails with an image pull error, the image is missing: build it (step 2).
- If runs fail with out-of-memory or "disk full" errors in SQL Server, the Docker VM is short of memory:
  raise the limit in step 1.
