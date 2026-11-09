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

A Feature Spec §12 lists **behaviour** commits. Split each behaviour into red then green (and refactor if it produced a file change). The tree must build after every commit.

## Use agentic-friendly TDD for development work

For production code, work test-first. Tests are the executable spec; do not start by writing implementation.

1. **Red** — Write the smallest failing test for one behaviour from the accepted spec. Run it and confirm it fails for the reason you expect.
2. **Green** — Write the minimum production code to make that test pass. Run the relevant tests. Do not add behaviour the test does not require.
3. **Refactor** — Clean up only while the suite is green. No new behaviour during refactor.

Treat each of red, green, and refactor as a step: commit after each one that produces a file change.

- One behaviour per cycle. Prefer the lowest test layer that can prove it (domain unit, then application, then functional/integration).
- Assert outcomes and invariants, not private implementation details.
- Do not skip the red run. A test that never failed is not a test.
- Do not make a failing test pass by weakening it unless the spec was wrong.
- Docs, config, and chores with no testable behaviour are exempt.

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
