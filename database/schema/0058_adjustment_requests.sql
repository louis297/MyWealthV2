-- Adjustment review (docs/features/adjustment-review.md §6).
-- An adjustment request is a Tenant's record of one adjustment a person asks for. It changes nothing in the ledger.
-- A TenantAdmin other than the requester approves it, and the approval posts the Adjustment with source
-- AdjustmentRequest (11) and the request's PublicId as source id.
-- Forward only, no back-fill: an Adjustment posted before this script keeps source Manual with no id (R13).
-- The applicator runs this script without a surrounding transaction.

-- R2, R6: one row per request; its fields never change after it is stored.
CREATE TABLE [AdjustmentRequests] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [Reason] int NOT NULL,
    [Memo] nvarchar(200) NOT NULL,
    [Reference] nvarchar(100) NULL,
    [CashAmount] decimal(18, 4) NULL,
    [InstrumentId] int NULL,
    [QuantityChange] decimal(18, 8) NULL,
    [CostChange] decimal(18, 4) NULL,
    [Status] int NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [RequestedAt] datetimeoffset NOT NULL,
    [DecisionAction] int NULL,
    [DecisionByUserId] int NULL,
    [DecisionAt] datetimeoffset NULL,
    [DecisionNote] nvarchar(200) NULL,
    [TransactionId] int NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_AdjustmentRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_AdjustmentRequests_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_AdjustmentRequests_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_AdjustmentRequests_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_AdjustmentRequests_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_AdjustmentRequests_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_AdjustmentRequests_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_AdjustmentRequests_Users_TenantId_RequestedByUserId]
        FOREIGN KEY ([TenantId], [RequestedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_AdjustmentRequests_Users_TenantId_DecisionByUserId]
        FOREIGN KEY ([TenantId], [DecisionByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_AdjustmentRequests_Reason] CHECK ([Reason] BETWEEN 0 AND 3),
    CONSTRAINT [CK_AdjustmentRequests_Status] CHECK ([Status] BETWEEN 0 AND 3),
    CONSTRAINT [CK_AdjustmentRequests_DecisionAction] CHECK ([DecisionAction] IS NULL OR [DecisionAction] BETWEEN 1 AND 3),
    -- A cash part, a holding part or both; a cash part is never 0.
    CONSTRAINT [CK_AdjustmentRequests_Part] CHECK (
        ([CashAmount] IS NOT NULL OR [InstrumentId] IS NOT NULL) AND ([CashAmount] IS NULL OR [CashAmount] <> 0)),
    -- The holding part has all three columns or none, and not both changes are 0.
    CONSTRAINT [CK_AdjustmentRequests_Holding] CHECK (
        ([InstrumentId] IS NULL AND [QuantityChange] IS NULL AND [CostChange] IS NULL)
        OR ([InstrumentId] IS NOT NULL AND [QuantityChange] IS NOT NULL AND [CostChange] IS NOT NULL
            AND ([QuantityChange] <> 0 OR [CostChange] <> 0))),
    -- R6: Requested has no decision; an end status has the decision that led to it, by a person, at a time.
    CONSTRAINT [CK_AdjustmentRequests_Decision] CHECK (
        ([Status] = 0 AND [DecisionAction] IS NULL AND [DecisionByUserId] IS NULL AND [DecisionAt] IS NULL AND [DecisionNote] IS NULL)
        OR ([Status] <> 0 AND [DecisionAction] = [Status] AND [DecisionByUserId] IS NOT NULL AND [DecisionAt] IS NOT NULL)),
    -- Invariant: Approved exactly when it has its Transaction.
    CONSTRAINT [CK_AdjustmentRequests_Transaction] CHECK (
        ([Status] = 1 AND [TransactionId] IS NOT NULL) OR ([Status] <> 1 AND [TransactionId] IS NULL)),
    -- R8: an approval or a rejection is made by someone other than the requester.
    CONSTRAINT [CK_AdjustmentRequests_FourEyes] CHECK (
        [DecisionAction] IS NULL OR [DecisionAction] = 3 OR [DecisionByUserId] <> [RequestedByUserId])
);

CREATE INDEX [IX_AdjustmentRequests_TenantId_RequestedAt] ON [AdjustmentRequests] ([TenantId], [RequestedAt] DESC);
-- R17: an account closure looks for the open requests of its Account.
CREATE INDEX [IX_AdjustmentRequests_AccountId_Status] ON [AdjustmentRequests] ([AccountId], [Status]);
CREATE UNIQUE INDEX [UX_AdjustmentRequests_TransactionId]
    ON [AdjustmentRequests] ([TransactionId])
    WHERE [TransactionId] IS NOT NULL;

-- R13: Transaction source kind AdjustmentRequest = 11, with the request's PublicId as source id.
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 11);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5, 6, 7, 8, 9, 10, 11) AND [SourcePublicId] IS NOT NULL));
