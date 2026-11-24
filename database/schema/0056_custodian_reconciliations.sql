-- Custodian reconciliations (docs/features/in-kind-transfers.md §6.2).
-- A stored run per safekeeping account: one line per symbol with the custodian's quantity, the platform's and five
-- components that add up to the difference. A run and its lines are never updated or deleted (R56).
-- Forward only, no back-fill.
-- The applicator runs this script without a surrounding transaction.

CREATE TABLE [CustodianReconciliations] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [SafekeepingAccountId] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [Trigger] int NOT NULL,
    [RanAt] datetimeoffset NOT NULL,
    [RanByUserId] int NULL,
    [Status] int NOT NULL,
    [PositionsAt] datetimeoffset NULL,
    [BreakCount] int NOT NULL,
    [TimingCount] int NOT NULL,
    [AgreedCount] int NOT NULL,
    [TransfersInFlight] int NOT NULL,
    [FillsNotDelivered] int NOT NULL,
    [FeedLastSucceededAt] datetimeoffset NULL,
    [FeedLastFailedAt] datetimeoffset NULL,
    [FeedLastFailure] int NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_CustodianReconciliations] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CustodianReconciliations_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_CustodianReconciliations_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_CustodianReconciliations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_CustodianReconciliations_SafekeepingAccounts_TenantId_SafekeepingAccountId]
        FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodianReconciliations_Users_TenantId_RanByUserId]
        FOREIGN KEY ([TenantId], [RanByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_CustodianReconciliations_Trigger] CHECK ([Trigger] BETWEEN 0 AND 1),
    -- R57, R58: a Scheduled run names no person; a Manual run names its person.
    CONSTRAINT [CK_CustodianReconciliations_RanBy] CHECK (
        ([Trigger] = 0 AND [RanByUserId] IS NULL) OR ([Trigger] = 1 AND [RanByUserId] IS NOT NULL)),
    CONSTRAINT [CK_CustodianReconciliations_Status] CHECK ([Status] BETWEEN 0 AND 2),
    -- R54: a NoPositions run has no positions time; every other run has one.
    CONSTRAINT [CK_CustodianReconciliations_PositionsAt] CHECK (
        ([Status] = 2 AND [PositionsAt] IS NULL) OR ([Status] <> 2 AND [PositionsAt] IS NOT NULL)),
    CONSTRAINT [CK_CustodianReconciliations_Counts] CHECK (
        [BreakCount] >= 0 AND [TimingCount] >= 0 AND [AgreedCount] >= 0 AND [TransfersInFlight] >= 0 AND [FillsNotDelivered] >= 0),
    CONSTRAINT [CK_CustodianReconciliations_FeedLastFailure] CHECK ([FeedLastFailure] IS NULL OR [FeedLastFailure] BETWEEN 0 AND 6)
);

CREATE INDEX [IX_CustodianReconciliations_TenantId_SafekeepingAccountId_RanAt]
    ON [CustodianReconciliations] ([TenantId], [SafekeepingAccountId], [RanAt] DESC);

CREATE TABLE [CustodianReconciliationLines] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [ReconciliationId] int NOT NULL,
    [Position] int NOT NULL,
    [Symbol] nvarchar(32) NOT NULL,
    [InstrumentId] int NULL,
    [CustodianQuantity] decimal(26, 8) NOT NULL,
    [PlatformQuantity] decimal(26, 8) NOT NULL,
    [Difference] decimal(26, 8) NOT NULL,
    [NotProcessed] decimal(26, 8) NOT NULL,
    [UnsettledTrades] decimal(26, 8) NOT NULL,
    [Suspense] decimal(26, 8) NOT NULL,
    [UnbookedFills] decimal(26, 8) NOT NULL,
    [Unexplained] decimal(26, 8) NOT NULL,
    [Class] int NOT NULL,
    CONSTRAINT [PK_CustodianReconciliationLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustodianReconciliationLines_CustodianReconciliations_TenantId_ReconciliationId]
        FOREIGN KEY ([TenantId], [ReconciliationId]) REFERENCES [CustodianReconciliations] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodianReconciliationLines_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [UQ_CustodianReconciliationLines_ReconciliationId_Position] UNIQUE ([ReconciliationId], [Position]),
    CONSTRAINT [CK_CustodianReconciliationLines_Position] CHECK ([Position] >= 1),
    CONSTRAINT [CK_CustodianReconciliationLines_Class] CHECK ([Class] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CustodianReconciliationLines_Difference] CHECK ([Difference] = [CustodianQuantity] - [PlatformQuantity]),
    -- R53: the five components add up exactly to the difference.
    CONSTRAINT [CK_CustodianReconciliationLines_Components] CHECK (
        [NotProcessed] + [UnsettledTrades] + [Suspense] + [UnbookedFills] + [Unexplained] = [Difference])
);
