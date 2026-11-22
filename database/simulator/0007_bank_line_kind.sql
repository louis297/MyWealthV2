-- The simulator bank, the kind of a line (docs/features/interest-run.md §6.3, R43, R44).
-- 0 is a transfer, 1 the bank's interest on the account. Existing lines are transfers.

ALTER TABLE [BankLines] ADD [Kind] int NOT NULL CONSTRAINT [DF_BankLines_Kind] DEFAULT (0);
EXEC(N'ALTER TABLE [BankLines] ADD CONSTRAINT [CK_BankLines_Kind] CHECK ([Kind] BETWEEN 0 AND 1);');
