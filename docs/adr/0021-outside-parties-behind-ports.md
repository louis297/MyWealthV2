---
title: "ADR 0021: Outside parties behind ports, and the simulator"
status: accepted
language: en
date: 2026-11-19
updated: 2026-11-20
related:
  - ../custodial-roadmap.md
  - ../architecture.md
  - ../features/simulator.md
  - ../features/deposit-matching.md
  - ../features/transaction-source.md
  - ../features/instrument-prices.md
  - ../features/fx-rates.md
  - 0008-schema-sql-as-source-of-truth.md
  - 0013-roles-authorization-single-user-table.md
  - 0014-openiddict-authorization-code-pkce.md
  - 0017-background-worker.md
  - 0018-transactional-outbox.md
  - 0019-fx-rate-source-and-refresh.md
---

# ADR 0021: Outside parties behind ports, and the simulator

Status: accepted (2026-11-19)

Roadmap item F2 of [custodial-roadmap.md](../custodial-roadmap.md) (step 2). It writes principles P1–P4, P9 and P10 and decisions D1, D2, D8–D10, D16, D17 and D19 of that note as one decision. The first port arrives with roadmap B3. The simulator host's first cut is [features/simulator.md](../features/simulator.md) (S0).

## Context

A Custodial account should get its numbers from platform-side flows driven by outside parties: a bank, a broker, a custodian, an issuer or data vendor. None of them exists here. The roadmap plans a separate simulator project that plays them, so the flows can be tested by hand and shown in a demo, and a real connection can replace it later.

The platform already reads two outside sources, and both follow one pattern: a port in Application (`IMarketData`, `IFxRate`), an HTTP adapter in Infrastructure (EODHD, Frankfurter), and a recurring `worker` job that pulls and stores (ADR 0017, ADR 0019, [instrument-prices.md](../features/instrument-prices.md)). A handler never calls the vendor. That pattern has no stored cursor, no idempotency on an outside id and no outgoing call, because a price feed needs none of them. A bank feed and a payment instruction need all three.

If every flow spec decided these on its own, the bank, broker and custodian flows would each pick a different retry, cursor and booking-date rule, and the simulator would have to serve all of them. The rules must be fixed once, before B3.

## Decision

The platform talks to every outside party through a port. A `worker` job pulls inbound data and stores it before any flow uses it. A flow books at the time it processes a stored record. The simulator is one adapter behind those ports, in its own host with its own database, and the platform does not know it is there.

### Ports and adapters

1. **One port per outside party role.** A port is an Application interface named after what the party does for the platform (bank feed, payments, broker, custodian statement, corporate actions), not after a vendor. Its adapters live in Infrastructure. Each flow spec defines the port it needs, and only that port.
2. **The simulator is an adapter.** No business code, Domain type, table or rule names the simulator. A real bank or broker later is another adapter behind the same port.
3. **Choosing an adapter is data or configuration, never the environment name.** For example, a client money account (roadmap B1) names the connection it is read through, and a simulated instrument's exchange suffix chooses the simulator price adapter (roadmap D11). Tests can therefore cover each choice in any environment.
4. **No outside call on the request path.** A `webapi` handler never calls an outside party. It writes its result and, when an outside call must follow, leaves it for a `worker` job (enqueued, or picked up by a recurring job). A read on `webapi` answers from local tables, as `IMarketData` and `IFxRate` already do.

### Pull, store, then use

5. **The platform pulls (D1).** Each feed is read by a recurring `worker` job. Outside parties do not push to the platform. There is no inbound endpoint and no machine sign-in on `webapi`. A simulated push may be added later as its own decision, once a flow runs on pull.
6. **Stored cursor per feed.** Each feed has a stored cursor: an opaque string that the party returned with its last page. The platform never parses it. A pull asks for the records after the cursor.
7. **Inbound records are stored before they are used (P3).** Each record the party returns is written once, to the inbound table of its flow (statement lines, fills, events). A unique index on (connection, outside id) rejects a second copy. The outside id is an opaque string set by the party; the adapter must guarantee that it is unique for that connection, for ever. The records of one page and the new cursor are written in one database transaction.
8. **Processing is a separate step.** A flow reads stored records by their status, never a port response directly. For each record it writes the ledger change and the record's new status in one database transaction, so a record is processed at most once. The processing step may run in the same job run as the pull, after the pull has committed.
9. **Flows set their source.** Every Transaction a flow writes has the flow's own transaction source kind and, where the spec gives one, a source id ([transaction-source.md](../features/transaction-source.md) R2, R3). Flows call the `Transaction` factories directly and do not go through the manual entry matrix (P5).

### Dates

10. **Book at processing time (D2).** A flow's Transaction is booked at the platform's time when the flow processes the record. The party's dates (value date, trade date, settlement date, record date) go into detail rows on the Transaction, the same pattern as `TransactionFxDetails` and `TransactionFeeDetails`. Booking at an outside date needs balance checks as of a past date, which needs replay (roadmap X2); revisit then.
11. **Two clocks (D9).** The platform keeps the real clock. An outside party's dates come from that party, as data in its records. The platform never moves its clock to follow a party, and a platform job whose result depends on a business date gets a manual "run for a date" trigger instead (roadmap D18).

### Outgoing calls

12. **Instructions call the party from the `worker`.** An instruction (a payment, an order, an in-kind transfer) is its own aggregate (P7). When it is ready to send, a `worker` job calls the port.
13. **Every outgoing call is idempotent.** The call carries the instruction's PublicId as its idempotency key. After an unknown outcome (a timeout, a lost response) the platform sends the same call with the same key again; it never sends a second key for the same instruction. The party must treat a repeat as the same call.
14. **The outcome comes back as a signal.** The Transaction is posted when the party confirms (a statement line, a fill), through the inbound path above, not from the response of the outgoing call.

### Failure

15. **A missing party is a normal state (D10).** Each adapter call has a timeout. When the party cannot be reached or answers with an error, the job run fails: it stores nothing for that feed, the cursor stays, and the next scheduled run continues from it. Pull jobs do not use automatic Hangfire retries; the schedule is the retry. Each connection to a party is its own recurring job, so one party's failure never stops another party's feeds, another job or `webapi`. When one connection serves several feeds (for example a bank's trust accounts), the job reads each feed on its own: a feed's failure is recorded on that feed and the job goes on with the next (amended 2026-11-20, [features/deposit-matching.md](../features/deposit-matching.md) R6, R7).
16. **The failure is visible.** A failed run is logged with the feed and the reason, and the feed's last success time is stored next to its cursor. A later operator page may show both; this ADR fixes only that they are stored.

### Credentials and network

17. **One credential per connection.** Each connection to an outside party has its own secret in configuration (Aspire parameter in Development, `.env` on the VPS), never in git. The platform does not issue credentials to outside parties and does not trust its own `identity` for calls to them: the party decides how it is called, as a real bank would.
18. **Party routes stay on the internal network (D17).** For the simulator, the routes the platform calls listen on their own port, which the reverse proxy never forwards to. They need the connection's machine credential.

### Tests

19. **Automated tests do not need the simulator process (P9).** Functional tests replace each port with an in-memory fake that the test feeds. Each adapter is tested against canned HTTP responses that follow the party's documented wire shape. A few end-to-end runs against the simulator are manual or opt-in, not part of the CI functional suite.
20. **Named scenarios may be shared with test fakes only.** When the simulator gains scenario definitions (roadmap S1), they live in a small library that the simulator and test projects reference. Platform production projects (`Domain`, `Application`, `Infrastructure`, `Web`, `IdentityHost`, `Worker`, the BFFs, `BackOffice`) never reference it.

### The simulator host

21. **Its own host and project.** Aspire resource `simulator`, project `src/Simulator`. It references no platform production project except `ServiceDefaults`, `Shared` (resource names) and the schema applicator library. It never references `Domain`, `Application`, `Infrastructure` or `Web`.
22. **Its own books (P10, D8).** Its own database on the same SQL Server, with its own versioned scripts in their own folder, applied by the same schema applicator at the simulator's start-up. The simulator never reads or writes the platform's tables, and the platform never reads the simulator's. Reconciliation (roadmap B7, T3) then compares two independent records.
23. **Its own clock and randomness.** The simulator's clock can move ahead of real time on command; every random choice is drawn from a stored seed. Same seed and same steps give the same signals.
24. **Deployed everywhere (D10, D19).** It runs in Development and on the VPS showcase, and serves every tenant's outside world. The operator may stop it at any time (rule 15). Revisit both before a real launch.
25. **Signed in through `identity` with a fifth role (D16).** Its control pages are an OIDC client of `identity`. Only a new platform-level role, `SimulatorOperator`, may sign in there, and that role may sign in nowhere else and has no `webapi` permission. This amends ADR 0013 decision 1 (four roles → five). SystemAdmin is not used, because it also holds all of Back Office.
26. **Two surfaces.** The control surface (for a person: clock, reset, faults, its own books) goes through the reverse proxy behind that sign-in. The party surface (for the platform: feeds, payments, orders) stays on the internal network (rule 18).

## Alternatives considered

| Option | Why not |
| --- | --- |
| Outside parties push to an inbound `webapi` endpoint (webhooks) | Needs a public inbound surface, machine sign-in on `webapi`, and replay protection before any flow works. Pull reuses the EODHD / Frankfurter pattern and its tests. Kept as a later option (D1) |
| Flows use the port response directly, without storing it | A resent page books twice, and reconciliation has no raw record to compare against |
| Book a flow's Transaction at the outside date (value date, trade date) | The ledger cannot check a balance as of a past date without replay. Booking at processing time keeps custody-modes R10 true for flows |
| Move the platform clock with the simulator | Makes every platform job, token lifetime and audit time depend on a test tool. Settlement and other outside events arrive as signals instead |
| Call outside parties from `webapi` handlers | Couples a request's success and latency to an outside party, and a failed call leaves a half-done request |
| Automatic Hangfire retries on pull jobs | A party that is down gets hit by every retry of every run. The next scheduled run already retries from the stored cursor |
| The platform calls the simulator with an `identity` token (client credentials grant) | A real bank would not trust the platform's identity server. It adds a grant to `identity` (ADR 0014) for a test tool. A connection secret owned by the party is the shape a real connection has |
| The simulator inside the platform (a `webapi` module, simulated rows in `MyWealthDbV2`) | Reconciliation would compare the platform with itself and always agree (P10). A real adapter could not replace it cleanly |
| A shared contracts library for the platform adapter and the simulator | Couples the two sides so a contract change cannot break either. Each side keeps its own wire types, as with EODHD; the party's spec documents the shape |
| SystemAdmin signs in to the simulator | SystemAdmin also has all of Back Office and every platform permission. A showcase operator account should not |
| A simulator per party (bank simulator, broker simulator, …) | More hosts, databases and deploy entries for one test tool. One host grows one party at a time (P11) |

## Consequences

- Every flow spec from B3 on follows rules 1–20 and does not re-decide them. A spec that needs an exception says so and amends this ADR.
- New tables arrive with their flows: an inbound table per flow, and the feed cursor table with the first feed (B3). This ADR creates none.
- The `worker` gains one recurring job per feed. Queues keep light isolation (ADR 0017).
- A real bank or broker later is a new adapter, a new connection secret and a configuration or data change. No flow changes.
- The simulator is a seventh .NET host and a second database. It needs a public hostname, a container, a Data Protection folder and secrets on the VPS ([features/simulator.md](../features/simulator.md)).
- The role enum grows to five values. ADR 0013 gains an amendment, `Users` gains a role value, `identity` gains a client and an allow-list row, and `webapi` refuses the new role.
- The schema applicator becomes a library that does not depend on `ApplicationDbContext`, so the simulator can use it without referencing `Infrastructure`.
- Data that arrives while the simulator is stopped is not lost: the simulator's own records stay in its database, and the next pull after it starts reads them from the stored cursor.

## Links

- Plan: [custodial-roadmap.md](../custodial-roadmap.md) §3, §4 F2, §11, §12
- Feature specs: [features/simulator.md](../features/simulator.md) (S0); [features/deposit-matching.md](../features/deposit-matching.md) (B3, B4: the first port, `IBankFeed`, and the feed cursor table)
- Related ADRs: [0008](0008-schema-sql-as-source-of-truth.md), [0013](0013-roles-authorization-single-user-table.md) (amended by rule 25), [0014](0014-openiddict-authorization-code-pkce.md), [0017](0017-background-worker.md), [0018](0018-transactional-outbox.md), [0019](0019-fx-rate-source-and-refresh.md)

## Amendments

- 2026-11-20 ([features/deposit-matching.md](../features/deposit-matching.md), accepted): rule 15 says "each connection is its own recurring job" instead of "each feed is its own recurring job". A bank connection serves one feed per client money account; one job per connection reads them one after another, each on its own, so the rule's purpose (one failure never stops another feed) holds without one Hangfire job per account. The polling interval is a setting of the connection.
- 2026-11-24 ([features/in-kind-transfers.md](../features/in-kind-transfers.md), accepted): the custodian's ports are `ICustodianFeed` (custody movements after a cursor per safekeeping account, with the custodian's positions on every page; rules 5–8) and `ICustodianTransfers` (one transfer instruction, keyed by the in-kind transfer's PublicId; rules 12–14), adapters `SimulatorCustodianFeed` and `SimulatorCustodianTransfers`, one job per custodian connection (`custodian-sync:{connection}`, rule 15). No rule changes.
