-- Bank connection of a client money account (docs/features/deposit-matching.md §6.1, R2).
-- The configured bank connection a client money account's statement is read through. It never changes.
-- Every existing row is read through the simulator (roadmap D19); the default fills them and is dropped again,
-- so a new row must name its connection. The applicator runs this script without a surrounding transaction, so
-- every step only does what is still missing. Forward only.

IF COL_LENGTH(N'ClientMoneyAccounts', N'BankConnection') IS NULL
BEGIN
    ALTER TABLE [ClientMoneyAccounts]
        ADD [BankConnection] nvarchar(50) NOT NULL
            CONSTRAINT [DF_ClientMoneyAccounts_BankConnection] DEFAULT N'simulator';
END;

IF OBJECT_ID(N'DF_ClientMoneyAccounts_BankConnection', N'D') IS NOT NULL
BEGIN
    ALTER TABLE [ClientMoneyAccounts] DROP CONSTRAINT [DF_ClientMoneyAccounts_BankConnection];
END;
