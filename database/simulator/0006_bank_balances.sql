-- The simulator bank, balances (docs/features/bank-reconciliation.md §6.2, R26–R29).
-- A line left out of the feed takes its place in the feed order but is never returned; it counts in the balance the
-- feed sends from its place on. Existing lines are delivered as before.

ALTER TABLE [BankLines] ADD [OmitFromFeed] bit NOT NULL CONSTRAINT [DF_BankLines_OmitFromFeed] DEFAULT (0);
