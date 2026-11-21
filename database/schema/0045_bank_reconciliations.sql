-- Bank reconciliation (docs/features/bank-reconciliation.md §6.1).
-- Turns on snapshot isolation for the reconciliation read (R6); existing reads keep READ COMMITTED. A feed cursor
-- gains the bank's statement balance, its time and the opening balance (R3, R4). Stored runs and their items
-- (R6–R15), never updated. The report scope Tenant and the bank-reconciliation catalog row (R22, R23). Forward only;
-- no back-fill.

ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;

ALTER TABLE [BankFeedCursors] ADD
    [Balance] decimal(18, 4) NULL,
    [BalanceAt] datetimeoffset NULL,
    [OpeningBalance] decimal(18, 4) NULL;

-- The new columns are referenced inside EXEC, so this batch compiles before they exist.
EXEC(N'
ALTER TABLE [BankFeedCursors] ADD CONSTRAINT [CK_BankFeedCursors_Balance] CHECK (
    ([Balance] IS NULL AND [BalanceAt] IS NULL) OR ([Balance] IS NOT NULL AND [BalanceAt] IS NOT NULL));
ALTER TABLE [BankFeedCursors] ADD CONSTRAINT [CK_BankFeedCursors_Opening] CHECK (
    [Balance] IS NULL OR [OpeningBalance] IS NOT NULL);
');

CREATE TABLE [BankReconciliations] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [ClientMoneyAccountId] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [Trigger] int NOT NULL,
    [RanAt] datetimeoffset NOT NULL,
    [RanByUserId] int NULL,
    [Status] int NOT NULL,
    [BankBalance] decimal(18, 4) NULL,
    [BalanceAt] datetimeoffset NULL,
    [OpeningBalance] decimal(18, 4) NULL,
    [StatementLinesTotal] decimal(18, 4) NULL,
    [CustodialCash] decimal(18, 4) NOT NULL,
    [Difference] decimal(18, 4) NULL,
    [BreakCount] int NOT NULL,
    [BreakTotal] decimal(18, 4) NOT NULL,
    [TimingCount] int NOT NULL,
    [TimingTotal] decimal(18, 4) NOT NULL,
    [KnownCount] int NOT NULL,
    [KnownTotal] decimal(18, 4) NOT NULL,
    [ReservedCash] decimal(18, 4) NOT NULL,
    [PaymentsInFlightCount] int NOT NULL,
    [PaymentsInFlightAmount] decimal(18, 4) NOT NULL,
    [FeedLastSucceededAt] datetimeoffset NULL,
    [FeedLastFailedAt] datetimeoffset NULL,
    [FeedLastFailure] int NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_BankReconciliations] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_BankReconciliations_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_BankReconciliations_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_BankReconciliations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_BankReconciliations_ClientMoneyAccounts_ClientMoneyAccountId]
        FOREIGN KEY ([ClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([Id]),
    CONSTRAINT [FK_BankReconciliations_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_BankReconciliations_Users_TenantId_RanByUserId]
        FOREIGN KEY ([TenantId], [RanByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_BankReconciliations_Trigger] CHECK ([Trigger] BETWEEN 0 AND 1),
    CONSTRAINT [CK_BankReconciliations_Status] CHECK ([Status] BETWEEN 0 AND 2),
    CONSTRAINT [CK_BankReconciliations_FeedLastFailure] CHECK ([FeedLastFailure] BETWEEN 0 AND 6),
    -- The daily job runs as nobody; a manual run names the person (R16, R17).
    CONSTRAINT [CK_BankReconciliations_RanBy] CHECK (
        ([Trigger] = 0 AND [RanByUserId] IS NULL) OR ([Trigger] = 1 AND [RanByUserId] IS NOT NULL)),
    -- A NoBankBalance run has no bank figures; every other run has all of them (R12).
    CONSTRAINT [CK_BankReconciliations_BankFigures] CHECK (
        ([Status] = 2 AND [BankBalance] IS NULL AND [BalanceAt] IS NULL AND [OpeningBalance] IS NULL
            AND [StatementLinesTotal] IS NULL AND [Difference] IS NULL)
        OR ([Status] <> 2 AND [BankBalance] IS NOT NULL AND [BalanceAt] IS NOT NULL AND [OpeningBalance] IS NOT NULL
            AND [StatementLinesTotal] IS NOT NULL AND [Difference] IS NOT NULL))
);

-- The overview and the history of one client money account (R19, R20).
CREATE INDEX [IX_BankReconciliations_TenantId_ClientMoneyAccountId_RanAt]
    ON [BankReconciliations] ([TenantId], [ClientMoneyAccountId], [RanAt] DESC);

-- The list of every run of a Tenant (R20).
CREATE INDEX [IX_BankReconciliations_TenantId_RanAt] ON [BankReconciliations] ([TenantId], [RanAt] DESC);

CREATE TABLE [BankReconciliationItems] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [ReconciliationId] int NOT NULL,
    [Position] int NOT NULL,
    [Kind] int NOT NULL,
    [Class] int NOT NULL,
    [Amount] decimal(18, 4) NOT NULL,
    [Count] int NOT NULL,
    [StatementLineId] int NULL,
    [Direction] int NULL,
    [ValueDate] date NULL,
    [LineStatus] int NULL,
    [UnmatchedReason] int NULL,
    [Note] nvarchar(200) NULL,
    [OtherClientMoneyAccountId] int NULL,
    CONSTRAINT [PK_BankReconciliationItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BankReconciliationItems_BankReconciliations_TenantId_ReconciliationId]
        FOREIGN KEY ([TenantId], [ReconciliationId]) REFERENCES [BankReconciliations] ([TenantId], [Id]),
    CONSTRAINT [FK_BankReconciliationItems_BankStatementLines_TenantId_StatementLineId]
        FOREIGN KEY ([TenantId], [StatementLineId]) REFERENCES [BankStatementLines] ([TenantId], [Id]),
    CONSTRAINT [FK_BankReconciliationItems_ClientMoneyAccounts_OtherClientMoneyAccountId]
        FOREIGN KEY ([OtherClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([Id]),
    CONSTRAINT [CK_BankReconciliationItems_Position] CHECK ([Position] >= 1),
    CONSTRAINT [CK_BankReconciliationItems_Kind] CHECK ([Kind] BETWEEN 0 AND 11),
    CONSTRAINT [CK_BankReconciliationItems_Class] CHECK ([Class] BETWEEN 0 AND 2),
    CONSTRAINT [CK_BankReconciliationItems_Count] CHECK ([Count] >= 1),
    -- A line item (Suspense, NotProcessed, ReturnInProgress, Ignored) carries the line as it was at the run; no
    -- other item names a line. The reason is set only with a line.
    CONSTRAINT [CK_BankReconciliationItems_Line] CHECK (
        ([Kind] IN (2, 3, 4, 6) AND [StatementLineId] IS NOT NULL AND [Direction] IS NOT NULL
            AND [ValueDate] IS NOT NULL AND [LineStatus] IS NOT NULL)
        OR ([Kind] NOT IN (2, 3, 4, 6) AND [StatementLineId] IS NULL AND [Direction] IS NULL
            AND [ValueDate] IS NULL AND [LineStatus] IS NULL AND [UnmatchedReason] IS NULL AND [Note] IS NULL)),
    CONSTRAINT [CK_BankReconciliationItems_UnmatchedReason] CHECK (
        [UnmatchedReason] IS NULL OR [LineStatus] = 2),
    -- Only an OtherClientMoneyAccount item names another client money account.
    CONSTRAINT [CK_BankReconciliationItems_OtherClientMoneyAccount] CHECK (
        ([Kind] = 11 AND [OtherClientMoneyAccountId] IS NOT NULL)
        OR ([Kind] <> 11 AND [OtherClientMoneyAccountId] IS NULL))
);

-- The items of a run in R13 order.
CREATE UNIQUE INDEX [UX_BankReconciliationItems_ReconciliationId_Position]
    ON [BankReconciliationItems] ([ReconciliationId], [Position]);

-- The report scope Tenant (R22) and its first report (R23).
ALTER TABLE [Reports] DROP CONSTRAINT [CK_Reports_Scope];
ALTER TABLE [Reports] ADD CONSTRAINT [CK_Reports_Scope] CHECK ([Scope] IN (N'Customer', N'Book', N'Tenant'));

IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Code] = N'bank-reconciliation')
    INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers], [IsActive]) VALUES
        ('3e7b9d14-6c2a-4f58-b0e1-9a4d2c7f8e33', N'bank-reconciliation', N'Bank reconciliation',
         N'A stored bank reconciliation of one client money account: the bank balance, the Custodial cash and the items between them.',
         N'Tenant', N'["manual","scheduled"]', 1);
