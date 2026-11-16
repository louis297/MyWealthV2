-- Account custody mode (docs/features/account-custody-mode.md §6, ADR 0020).
-- Every Account gets CustodyMode (External = 0, Custodial = 1). Existing rows become External from a
-- temporary default; the default is then dropped, so an insert that does not name the column fails.

ALTER TABLE [Accounts] ADD [CustodyMode] int NOT NULL
    CONSTRAINT [DF_Accounts_CustodyMode] DEFAULT (0);

EXEC(N'
ALTER TABLE [Accounts] DROP CONSTRAINT [DF_Accounts_CustodyMode];
ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Accounts_CustodyMode] CHECK ([CustodyMode] BETWEEN 0 AND 1);
');
