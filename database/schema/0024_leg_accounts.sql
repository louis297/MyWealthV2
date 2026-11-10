-- Leg accounts (docs/features/leg-accounts.md).
-- Every cash and security leg carries its own AccountId; the Transaction header no longer does.
-- This script back-fills each leg from its header on purpose: without it the column cannot be NOT NULL.

ALTER TABLE [TransactionCashLegs] ADD [AccountId] int NULL;
ALTER TABLE [TransactionSecurityLegs] ADD [AccountId] int NULL;

EXEC(N'
UPDATE leg
SET leg.[AccountId] = header.[AccountId]
FROM [TransactionCashLegs] leg
JOIN [Transactions] header ON header.[Id] = leg.[TransactionId];

UPDATE leg
SET leg.[AccountId] = header.[AccountId]
FROM [TransactionSecurityLegs] leg
JOIN [Transactions] header ON header.[Id] = leg.[TransactionId];
');

EXEC(N'
ALTER TABLE [TransactionCashLegs] ALTER COLUMN [AccountId] int NOT NULL;
ALTER TABLE [TransactionSecurityLegs] ALTER COLUMN [AccountId] int NOT NULL;
');

EXEC(N'
ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [UQ_TransactionCashLegs_TransactionId];
ALTER TABLE [TransactionCashLegs] ADD
    CONSTRAINT [UQ_TransactionCashLegs_TransactionId_AccountId] UNIQUE ([TransactionId], [AccountId]),
    CONSTRAINT [FK_TransactionCashLegs_Accounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]);
CREATE INDEX [IX_TransactionCashLegs_AccountId]
    ON [TransactionCashLegs] ([AccountId]) INCLUDE ([Amount]);

ALTER TABLE [TransactionSecurityLegs] DROP CONSTRAINT [UQ_TransactionSecurityLegs_TransactionId_InstrumentId];
ALTER TABLE [TransactionSecurityLegs] ADD
    CONSTRAINT [UQ_TransactionSecurityLegs_TransactionId_AccountId_InstrumentId]
        UNIQUE ([TransactionId], [AccountId], [InstrumentId]),
    CONSTRAINT [FK_TransactionSecurityLegs_Accounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]);
CREATE INDEX [IX_TransactionSecurityLegs_AccountId_InstrumentId]
    ON [TransactionSecurityLegs] ([AccountId], [InstrumentId]) INCLUDE ([Quantity], [CostAmount]);
');

DROP INDEX [IX_Transactions_TenantId_AccountId_BookedAt] ON [Transactions];
ALTER TABLE [Transactions] DROP CONSTRAINT [FK_Transactions_Accounts_AccountId];
ALTER TABLE [Transactions] DROP COLUMN [AccountId];
CREATE INDEX [IX_Transactions_TenantId_BookedAt] ON [Transactions] ([TenantId], [BookedAt]);
