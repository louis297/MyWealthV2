# Agent rules (Claude Code)

This file is for agents working in the **MyWealthV2 repository**. Design lives in `docs/`. Do not copy Feature Specs or ADRs into this file.

## `draft/` is off-limits

Do not read, list, search, edit, create, delete, move, or otherwise touch anything under `draft/`. Treat that directory as if it does not exist. Work from `docs/` and the rest of the repo instead.

## Language

- Chat may be in English, Chinese, or a mix of both. Reply in whichever language the user is using.
- Everything written to the repo is **English only**: docs, code, comments, test names, commit messages, and log or error text.
- When a design idea is discussed in Chinese, write it in English using the terms in `docs/glossary.md`. If no term fits, add one to the glossary. Do not leave Chinese words in brackets in the docs.

## Docs tasks

When the user says it is a docs task (for example "It's a docs task", "docs only", or "这是文档任务"), we are discussing design. The task stays in docs mode until the user says otherwise.

- Change files under `docs/` only. Do not change code, tests, `database/schema/`, config, or CI.
- You may read code to check how something works now. Do not "fix" code you find that disagrees with a doc. Note it in the doc or tell the user.
- All design docs live under `docs/`. Do not put doc files at the repo root, next to code, or in a new top-level folder. The only exceptions are the existing root `README.md`, `CLAUDE.md`, and tool READMEs such as `.devcontainer/README.md`.
- Follow the doc conventions in `docs/README.md`: front matter, `status`, glossary terms.
- Do not set a spec's `status` to `accepted` unless the user says so. `accepted` makes it a build contract.
- Commit each docs change as its own step (see below). The TDD rules do not apply.

## What to implement

- Implement only **`accepted`** specs for the **current phase** (`docs/README.md`, `docs/function-plan.md`, the slice Feature Spec or portal spec).
- `draft`, `review`, and `note` are not build contracts. Do not create tables, routes, or handlers from them.
- Do not add Phase-2 ledger tables, routes, or empty spec files (instruments, accounts, holdings, transactions, net worth) until that phase is opened.
- Schema truth is `database/schema/NNNN_*.sql`, applied by the schema applicator. Do not add EF migrations. Do not use `EnsureDeleted` / `EnsureCreated` as the normal boot path.
- HTTP ids are `PublicId`. Internal ints never appear on the wire.
- From Tenants onward, every tenant-scoped slice includes cross-tenant isolation tests (two-tenant fixture; other tenant’s id → 404, not 403).
- When code and an accepted doc disagree, change the code or the doc in the **same** change.
- New glossary terms land in `docs/glossary.md` in the same change.
- Portal UI belongs under `docs/portals/`. Do not invent backend routes from a portal page note.

Read order: `docs/README.md` → glossary → function-plan → the slice spec → architecture / domain / database / api-design / ADRs only if needed.

## Always commit each step

Create a git commit after every discrete step of work. Do not leave completed steps uncommitted, and do not batch multiple steps into one commit.

- Commit as soon as the step is done and verified, before starting the next step.
- Stage only the files that belong to that step; leave unrelated changes unstaged.
- Use a short, factual commit message that describes what the step did (repo style: `Add failing tests for …` then `Add …`).
- Never commit secrets, credentials, `.env` files, or generated artifacts that should stay untracked.
- If a step produced no file changes, skip the commit.
- Do not amend, rebase, or rewrite history unless the user explicitly asks.

A Feature Spec §12 lists **behaviour** commits. Split each behaviour into a red commit and a green commit, plus a refactor commit if the refactor changed files. The tree must **compile** after every commit. A red commit has failing tests on purpose; every green or refactor commit must pass the test suite.

## Use agentic-friendly TDD for development work

For production code, work test-first. Tests are the executable spec; do not start by writing implementation.

1. **Red**: Write the failing tests for one behaviour from the accepted spec. Run them and confirm they fail **on an assertion**, for the reason you expect. A compile error is not a valid red.
2. **Green**: Write the minimum production code that makes those tests pass. Run the focused tests, then the test projects the change touches, before you commit. Do not add behaviour the tests do not require.
3. **Refactor** (optional): Clean up only while the tests are green. Add no new behaviour. Skip the step if nothing needs cleaning.

Rules:

- **One behaviour per cycle, not one test.** A behaviour may need several test cases (happy path, validation cases, the cross-tenant 404). Write them together in one red step.
- **Stubs in red are allowed.** In C#, a test that uses a new type will not compile. The red commit may add the needed signatures (types, members, endpoint shells) with bodies that throw `NotImplementedException` or return a default, so that the tree compiles and the test fails on its assertion. The stubs contain no logic.
- **Prefer the lowest test layer** that can prove the behaviour: domain unit, then application, then functional/integration. Use the functional layer for HTTP status codes, auth, tenant isolation, and SQL schema behaviour.
- Assert outcomes and invariants, not private implementation details.
- Do not skip the red run. A test that never failed is not a test.
- Do not make a failing test pass by weakening it unless the spec was wrong. If it was, fix the spec in the same change and tell the user.
- **Bug fixes** start with a test that reproduces the bug (red), then the fix (green).
- **Exempt from test-first**: docs, config, CI, DI or wiring with no logic of its own, generated files, pure renames or moves, and refactors already covered by green tests. Still build and run the affected tests before you commit.
- **Spikes**: if you need to try out an unknown API or library before you can write a sensible test, you may spike it. Tell the user it is a spike, do not commit it, then throw it away and start the red step.

## Stop and hand off (mandatory)

You are implementing, not exploring indefinitely.

Count a **variant** as: one distinct hypothesis about the cause, followed by a code change
and a test run that still fails the same assertion. Compiles, formatter noise, and
re-running the same patch do not increment the count.

After **3 variants** on the same failing assertion (or ~45 minutes wall-clock on that
assertion, whichever first):

1. **Stop.** Do not start a fourth approach. Do not weaken or delete the test.
   Do not “fix” a different layer to make the test green (login, authorize, skip,
   `Ignore`, change expected status).
2. **Revert experimental patches** that did not land in a good commit, so the tree
   matches the last known-good commit plus any already-accepted slices.
3. **End the turn** with the report below and nothing after it that continues coding.
4. Wait for a human review or a brief pasted from web chat. Do not resume that bug
   until the next message explicitly says to continue and names the allowed change.

If you are about to retry the same idea with a different knob
(`TransactionScope.Suppress`, extra log, move the same call one method up), that is
still the same variant. Stop.

### Handoff report (use this shape)

```
## Stopped

- Task / slice:
- Failing tests (exact names):
- Last good commit:

## What is true (do not re-prove)

- …
- …

## What we tried (do not retry)

| Variant | Change | Result |
| --- | --- | --- |
| 1 | | |
| 2 | | |
| 3 | | |

## Likely cause (one paragraph)

## What we will not touch

- Wrong layer / out of spec / would only hide the assertion

## Allowed next step (only after review)

- Files that may change:
- Conditions that must hold together (if two changes are required, say so):
- First command to run:
```
