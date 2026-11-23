-- Migration batches (docs/features/migration-batches.md §6).
-- A migration batch is a Tenant's load of opening positions from a previous provider into empty Custodial accounts:
-- a header, control totals, one entry per Account with its cash and holdings, and the statement lines that fund it.
-- Its post writes an Opening and an OpeningHoldings per entry with source Migration (9) and a migration detail.
-- Forward only, no back-fill: no batch and no Transaction with source Migration exists before this script.
-- The applicator runs this script without a surrounding transaction. Statements that name a column added here run
-- inside EXEC, so each batch compiles before the column exists.

CREATE TABLE [MigrationBatches] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [Status] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [PreviousProvider] nvarchar(100) NOT NULL,
    [AsOfDate] date NOT NULL,
    [ControlAccountCount] int NOT NULL,
    [SubmittedByUserId] int NULL,
    [SubmittedAt] datetimeoffset NULL,
    [ReturnedByUserId] int NULL,
    [ReturnedAt] datetimeoffset NULL,
    [ReturnNote] nvarchar(200) NULL,
    [PostedByUserId] int NULL,
    [PostedAt] datetimeoffset NULL,
    [CancelledByUserId] int NULL,
    [CancelledAt] datetimeoffset NULL,
    [CancelNote] nvarchar(200) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MigrationBatches] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_MigrationBatches_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_MigrationBatches_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_MigrationBatches_Users_TenantId_SubmittedByUserId]
        FOREIGN KEY ([TenantId], [SubmittedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatches_Users_TenantId_ReturnedByUserId]
        FOREIGN KEY ([TenantId], [ReturnedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatches_Users_TenantId_PostedByUserId]
        FOREIGN KEY ([TenantId], [PostedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatches_Users_TenantId_CancelledByUserId]
        FOREIGN KEY ([TenantId], [CancelledByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    -- MigrationBatchStatus: Draft, Submitted, Posted, Cancelled.
    CONSTRAINT [CK_MigrationBatches_Status] CHECK ([Status] BETWEEN 0 AND 3),
    CONSTRAINT [CK_MigrationBatches_ControlAccountCount] CHECK ([ControlAccountCount] >= 0),
    -- R17, R18: submitted by and at are both set while Submitted or Posted and both null on a Draft; a cancel keeps them.
    CONSTRAINT [CK_MigrationBatches_Submitted] CHECK (
        (([SubmittedByUserId] IS NULL AND [SubmittedAt] IS NULL) OR ([SubmittedByUserId] IS NOT NULL AND [SubmittedAt] IS NOT NULL))
        AND ([Status] NOT IN (1, 2) OR [SubmittedAt] IS NOT NULL)
        AND ([Status] <> 0 OR [SubmittedAt] IS NULL)),
    -- R18: the last return, all three or none.
    CONSTRAINT [CK_MigrationBatches_Returned] CHECK (
        ([ReturnedByUserId] IS NULL AND [ReturnedAt] IS NULL AND [ReturnNote] IS NULL)
        OR ([ReturnedByUserId] IS NOT NULL AND [ReturnedAt] IS NOT NULL AND [ReturnNote] IS NOT NULL)),
    -- R20, R23: posted by and at iff Posted, by someone other than the submitter.
    CONSTRAINT [CK_MigrationBatches_Posted] CHECK (
        ([Status] = 2 AND [PostedByUserId] IS NOT NULL AND [PostedAt] IS NOT NULL AND [PostedByUserId] <> [SubmittedByUserId])
        OR ([Status] <> 2 AND [PostedByUserId] IS NULL AND [PostedAt] IS NULL)),
    -- R19: cancelled by, at and note iff Cancelled.
    CONSTRAINT [CK_MigrationBatches_Cancelled] CHECK (
        ([Status] = 3 AND [CancelledByUserId] IS NOT NULL AND [CancelledAt] IS NOT NULL AND [CancelNote] IS NOT NULL)
        OR ([Status] <> 3 AND [CancelledByUserId] IS NULL AND [CancelledAt] IS NULL AND [CancelNote] IS NULL))
);

CREATE INDEX [IX_MigrationBatches_TenantId_Status_Created] ON [MigrationBatches] ([TenantId], [Status], [Created] DESC);

CREATE TABLE [MigrationBatchControlTotals] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [MigrationBatchId] int NOT NULL,
    [Kind] int NOT NULL,
    [Currency] char(3) NULL,
    [InstrumentId] int NULL,
    [Amount] decimal(26, 8) NOT NULL,
    CONSTRAINT [PK_MigrationBatchControlTotals] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_MigrationBatchControlTotals_MigrationBatchId_Kind_Currency_InstrumentId]
        UNIQUE ([MigrationBatchId], [Kind], [Currency], [InstrumentId]),
    CONSTRAINT [FK_MigrationBatchControlTotals_MigrationBatches_TenantId_MigrationBatchId]
        FOREIGN KEY ([TenantId], [MigrationBatchId]) REFERENCES [MigrationBatches] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatchControlTotals_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    -- MigrationControlTotalKind: Cash, Quantity, Cost.
    CONSTRAINT [CK_MigrationBatchControlTotals_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
    -- A currency for Cash and Cost, an Instrument for Quantity.
    CONSTRAINT [CK_MigrationBatchControlTotals_Target] CHECK (
        ([Kind] IN (0, 2) AND [Currency] IS NOT NULL AND [InstrumentId] IS NULL)
        OR ([Kind] = 1 AND [Currency] IS NULL AND [InstrumentId] IS NOT NULL)),
    CONSTRAINT [CK_MigrationBatchControlTotals_Amount] CHECK ([Amount] >= 0)
);

CREATE TABLE [MigrationBatchEntries] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [MigrationBatchId] int NOT NULL,
    [Position] int NOT NULL,
    [AccountId] int NOT NULL,
    [ExternalReference] nvarchar(100) NULL,
    [CashAmount] decimal(18, 4) NULL,
    [OpeningTransactionId] int NULL,
    [OpeningHoldingsTransactionId] int NULL,
    CONSTRAINT [PK_MigrationBatchEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_MigrationBatchEntries_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [UQ_MigrationBatchEntries_MigrationBatchId_AccountId] UNIQUE ([MigrationBatchId], [AccountId]),
    CONSTRAINT [UQ_MigrationBatchEntries_MigrationBatchId_Position] UNIQUE ([MigrationBatchId], [Position]),
    CONSTRAINT [FK_MigrationBatchEntries_MigrationBatches_TenantId_MigrationBatchId]
        FOREIGN KEY ([TenantId], [MigrationBatchId]) REFERENCES [MigrationBatches] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatchEntries_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatchEntries_Transactions_TenantId_OpeningTransactionId]
        FOREIGN KEY ([TenantId], [OpeningTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatchEntries_Transactions_TenantId_OpeningHoldingsTransactionId]
        FOREIGN KEY ([TenantId], [OpeningHoldingsTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_MigrationBatchEntries_CashAmount] CHECK ([CashAmount] > 0)
);

CREATE INDEX [IX_MigrationBatchEntries_TenantId_AccountId] ON [MigrationBatchEntries] ([TenantId], [AccountId]);

CREATE TABLE [MigrationBatchHoldings] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [MigrationBatchEntryId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [Cost] decimal(18, 4) NOT NULL,
    CONSTRAINT [PK_MigrationBatchHoldings] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_MigrationBatchHoldings_MigrationBatchEntryId_InstrumentId] UNIQUE ([MigrationBatchEntryId], [InstrumentId]),
    CONSTRAINT [FK_MigrationBatchHoldings_MigrationBatchEntries_TenantId_MigrationBatchEntryId]
        FOREIGN KEY ([TenantId], [MigrationBatchEntryId]) REFERENCES [MigrationBatchEntries] ([TenantId], [Id]),
    CONSTRAINT [FK_MigrationBatchHoldings_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [CK_MigrationBatchHoldings_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_MigrationBatchHoldings_Cost] CHECK ([Cost] >= 0)
);

-- R22: where a migrated opening balance came from; at most one row per Transaction.
CREATE TABLE [TransactionMigrationDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [AsOfDate] date NOT NULL,
    [PreviousProvider] nvarchar(100) NOT NULL,
    CONSTRAINT [PK_TransactionMigrationDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionMigrationDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionMigrationDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id])
);

CREATE INDEX [IX_TransactionMigrationDetails_TenantId] ON [TransactionMigrationDetails] ([TenantId]);

-- R27: Transaction source kind Migration = 9, with the batch's PublicId as source id.
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 9);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5, 6, 7, 8, 9) AND [SourcePublicId] IS NOT NULL));

-- R13, R16: a statement line matched to a migration batch; method Migration (6), with no Account and no Transaction.
ALTER TABLE [BankStatementLines] ADD [MigrationBatchId] int NULL;
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_MatchMethod];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_Match];
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_MatchMethod] CHECK ([MatchMethod] BETWEEN 0 AND 6);
EXEC(N'
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [FK_BankStatementLines_MigrationBatches_TenantId_MigrationBatchId]
    FOREIGN KEY ([TenantId], [MigrationBatchId]) REFERENCES [MigrationBatches] ([TenantId], [Id]);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_Match] CHECK (
    ([Status] = 1 AND [MatchMethod] IS NOT NULL AND [MatchedAt] IS NOT NULL
        AND (([MatchedAccountId] IS NOT NULL AND [MatchedTransactionId] IS NOT NULL AND [TradeSettlementId] IS NULL
                AND [MigrationBatchId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [RelatedLineId] IS NOT NULL
                AND [TradeSettlementId] IS NULL AND [MigrationBatchId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [TradeSettlementId] IS NOT NULL
                AND [MigrationBatchId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [TradeSettlementId] IS NULL
                AND [MigrationBatchId] IS NOT NULL)))
    OR ([Status] <> 1 AND [MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL
        AND [MatchMethod] IS NULL AND [MatchedAt] IS NULL AND [TradeSettlementId] IS NULL AND [MigrationBatchId] IS NULL));
CREATE INDEX [IX_BankStatementLines_MigrationBatchId] ON [BankStatementLines] ([MigrationBatchId])
    WHERE [MigrationBatchId] IS NOT NULL;
');

-- R32: MigrationNotPosted (19) carries its line.
ALTER TABLE [BankReconciliationItems] DROP CONSTRAINT [CK_BankReconciliationItems_Kind];
ALTER TABLE [BankReconciliationItems] DROP CONSTRAINT [CK_BankReconciliationItems_Line];
ALTER TABLE [BankReconciliationItems] ADD CONSTRAINT [CK_BankReconciliationItems_Kind] CHECK ([Kind] BETWEEN 0 AND 19);
ALTER TABLE [BankReconciliationItems] ADD CONSTRAINT [CK_BankReconciliationItems_Line] CHECK (
    ([Kind] IN (2, 3, 4, 6, 12, 19) AND [StatementLineId] IS NOT NULL AND [Direction] IS NOT NULL
        AND [ValueDate] IS NOT NULL AND [LineStatus] IS NOT NULL)
    OR ([Kind] NOT IN (2, 3, 4, 6, 12, 19) AND [StatementLineId] IS NULL AND [Direction] IS NULL
        AND [ValueDate] IS NULL AND [LineStatus] IS NULL AND [UnmatchedReason] IS NULL AND [Note] IS NULL));
