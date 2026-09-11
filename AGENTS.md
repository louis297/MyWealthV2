# Agent rules

## `draft/` is off-limits

Do not read, list, search, edit, create, delete, move, or otherwise touch anything under `draft/`. Treat that directory as if it does not exist. Work from `docs/` and the rest of the repo instead.

## Always commit each step

Create a git commit after every discrete step of work. Do not leave completed steps uncommitted, and do not batch multiple steps into one commit.

- Commit as soon as the step is done and verified, before starting the next step.
- Stage only the files that belong to that step; leave unrelated changes unstaged.
- Use a short, factual commit message that describes what the step did.
- Never commit secrets, credentials, `.env` files, or generated artifacts that should stay untracked.
- If a step produced no file changes, skip the commit.
- Do not amend, rebase, or rewrite history unless the user explicitly asks.

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
