-- Adjustments (docs/features/adjustments.md §6).
-- Widens the Transaction type check for Adjustment (17) and adds the reason row each Adjustment and its Reversal
-- carry. Forward only; no existing row changes.

ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_Type];

ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_Type]
    CHECK ([Type] BETWEEN 0 AND 17);

CREATE TABLE [TransactionAdjustmentDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [Reason] int NOT NULL,
    CONSTRAINT [PK_TransactionAdjustmentDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionAdjustmentDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionAdjustmentDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_TransactionAdjustmentDetails_Reason] CHECK ([Reason] BETWEEN 0 AND 3)
);

CREATE INDEX [IX_TransactionAdjustmentDetails_TenantId] ON [TransactionAdjustmentDetails] ([TenantId]);
