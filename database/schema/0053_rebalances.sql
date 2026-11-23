-- Rebalance through orders (docs/features/rebalances.md §6).
-- A rebalance is a stored run per Account: a proposal with its own copy of the portfolio's targets and one line per
-- Instrument held or targeted, a person's confirmation, then ordinary orders in waves. An order names the rebalance
-- that placed it. Forward only, no back-fill: no rebalance is created for a run before this script, existing orders
-- have no rebalance, and existing Transactions keep source Rebalance.
-- The applicator runs this script without a surrounding transaction. Statements that name a column added here run
-- inside EXEC, so each batch compiles before the column exists.

-- The composite key the tenant FK from Rebalances needs (the table had none).
ALTER TABLE [AccountPortfolioAssignments]
    ADD CONSTRAINT [UQ_AccountPortfolioAssignments_TenantId_Id] UNIQUE ([TenantId], [Id]);

CREATE TABLE [Rebalances] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [PortfolioId] int NOT NULL,
    [AssignmentId] int NOT NULL,
    [Trigger] int NOT NULL,
    [Status] int NOT NULL,
    [BaseCurrency] char(3) NOT NULL,
    [TotalValue] decimal(18, 4) NULL,
    [Cash] decimal(18, 4) NULL,
    [MinimumTrade] decimal(18, 4) NOT NULL,
    [CashResidualPercent] decimal(5, 2) NOT NULL,
    [Connection] nvarchar(50) NULL,
    [Waves] int NOT NULL,
    [BuyWaves] int NOT NULL,
    [FailureCode] nvarchar(50) NULL,
    [FailureMessage] nvarchar(500) NULL,
    [ProposedByUserId] int NULL,
    [ProposedAt] datetimeoffset NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [ConfirmedByUserId] int NULL,
    [ConfirmedAt] datetimeoffset NULL,
    [CancelRequestedAt] datetimeoffset NULL,
    [CancelRequestedByUserId] int NULL,
    [CancelNote] nvarchar(200) NULL,
    [CancelReason] int NULL,
    [FollowUp] bit NOT NULL CONSTRAINT [DF_Rebalances_FollowUp] DEFAULT 0,
    [EndedAt] datetimeoffset NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Rebalances] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Rebalances_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Rebalances_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_Rebalances_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Rebalances_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_Rebalances_Portfolios_TenantId_PortfolioId]
        FOREIGN KEY ([TenantId], [PortfolioId]) REFERENCES [Portfolios] ([TenantId], [Id]),
    CONSTRAINT [FK_Rebalances_AccountPortfolioAssignments_TenantId_AssignmentId]
        FOREIGN KEY ([TenantId], [AssignmentId]) REFERENCES [AccountPortfolioAssignments] ([TenantId], [Id]),
    CONSTRAINT [FK_Rebalances_Users_TenantId_ProposedByUserId]
        FOREIGN KEY ([TenantId], [ProposedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_Rebalances_Users_TenantId_ConfirmedByUserId]
        FOREIGN KEY ([TenantId], [ConfirmedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_Rebalances_Users_TenantId_CancelRequestedByUserId]
        FOREIGN KEY ([TenantId], [CancelRequestedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    -- RebalanceTrigger: Assign, Account, Portfolio, FollowUp (R5).
    CONSTRAINT [CK_Rebalances_Trigger] CHECK ([Trigger] BETWEEN 0 AND 3),
    -- RebalanceStatus: Proposed, Selling, Buying, Completed, Incomplete, Cancelled, NoChange, Failed (R3).
    CONSTRAINT [CK_Rebalances_Status] CHECK ([Status] BETWEEN 0 AND 7),
    -- R14: at most two buy waves, and never more buy waves than waves.
    CONSTRAINT [CK_Rebalances_Waves] CHECK ([Waves] >= 0 AND [BuyWaves] >= 0 AND [BuyWaves] <= 2 AND [BuyWaves] <= [Waves]),
    -- A failure code and message iff Incomplete or Failed.
    CONSTRAINT [CK_Rebalances_Failure] CHECK (
        ([Status] IN (4, 7) AND [FailureCode] IS NOT NULL AND [FailureMessage] IS NOT NULL)
        OR ([Status] NOT IN (4, 7) AND [FailureCode] IS NULL AND [FailureMessage] IS NULL)),
    -- Confirmed by and at: both or neither, and both once it placed orders or ended after them.
    CONSTRAINT [CK_Rebalances_Confirmed] CHECK (
        (([ConfirmedByUserId] IS NULL AND [ConfirmedAt] IS NULL) OR ([ConfirmedByUserId] IS NOT NULL AND [ConfirmedAt] IS NOT NULL))
        AND ([Status] NOT IN (1, 2, 3, 4) OR [ConfirmedAt] IS NOT NULL)),
    -- RebalanceCancelReason: Requested, Superseded, Unassigned; set iff Cancelled.
    CONSTRAINT [CK_Rebalances_CancelReason] CHECK (
        ([Status] = 5 AND [CancelReason] IS NOT NULL AND [CancelReason] BETWEEN 0 AND 2) OR ([Status] <> 5 AND [CancelReason] IS NULL)),
    -- Ended iff final.
    CONSTRAINT [CK_Rebalances_EndedAt] CHECK (
        ([Status] >= 3 AND [EndedAt] IS NOT NULL) OR ([Status] < 3 AND [EndedAt] IS NULL))
);

-- R4: at most one open rebalance per Account.
CREATE UNIQUE INDEX [UX_Rebalances_AccountId_Open] ON [Rebalances] ([AccountId]) WHERE [Status] IN (0, 1, 2);
CREATE INDEX [IX_Rebalances_TenantId_ProposedAt] ON [Rebalances] ([TenantId], [ProposedAt] DESC);
CREATE INDEX [IX_Rebalances_PortfolioId_Status] ON [Rebalances] ([PortfolioId], [Status]);
-- R17: the rebalances in progress of a connection, for the advance step.
CREATE INDEX [IX_Rebalances_Connection_InProgress] ON [Rebalances] ([Connection], [Id]) WHERE [Status] IN (1, 2);

CREATE TABLE [RebalanceTargets] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [RebalanceId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [TargetWeight] decimal(5, 2) NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_RebalanceTargets] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_RebalanceTargets_RebalanceId_InstrumentId] UNIQUE ([RebalanceId], [InstrumentId]),
    CONSTRAINT [FK_RebalanceTargets_Rebalances_TenantId_RebalanceId]
        FOREIGN KEY ([TenantId], [RebalanceId]) REFERENCES [Rebalances] ([TenantId], [Id]),
    CONSTRAINT [FK_RebalanceTargets_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [CK_RebalanceTargets_TargetWeight] CHECK ([TargetWeight] > 0 AND [TargetWeight] <= 100)
);

CREATE TABLE [RebalanceLines] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [RebalanceId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [Weight] decimal(5, 2) NOT NULL,
    [TargetWeight] decimal(5, 2) NULL,
    [Side] int NULL,
    [TradeQuantity] decimal(18, 8) NULL,
    [ReferencePrice] decimal(18, 8) NULL,
    [ReferencePriceDate] date NULL,
    [EstimatedGross] decimal(18, 4) NULL,
    [EstimatedFee] decimal(18, 4) NULL,
    [Currency] char(3) NOT NULL,
    CONSTRAINT [PK_RebalanceLines] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_RebalanceLines_RebalanceId_InstrumentId] UNIQUE ([RebalanceId], [InstrumentId]),
    CONSTRAINT [FK_RebalanceLines_Rebalances_TenantId_RebalanceId]
        FOREIGN KEY ([TenantId], [RebalanceId]) REFERENCES [Rebalances] ([TenantId], [Id]),
    CONSTRAINT [FK_RebalanceLines_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    -- OrderSide: Buy, Sell.
    CONSTRAINT [CK_RebalanceLines_Side] CHECK ([Side] BETWEEN 0 AND 1),
    -- The five trade columns are set iff Side is set.
    CONSTRAINT [CK_RebalanceLines_Trade] CHECK (
        ([Side] IS NOT NULL AND [TradeQuantity] IS NOT NULL AND [ReferencePrice] IS NOT NULL
            AND [ReferencePriceDate] IS NOT NULL AND [EstimatedGross] IS NOT NULL AND [EstimatedFee] IS NOT NULL)
        OR ([Side] IS NULL AND [TradeQuantity] IS NULL AND [ReferencePrice] IS NULL
            AND [ReferencePriceDate] IS NULL AND [EstimatedGross] IS NULL AND [EstimatedFee] IS NULL))
);

-- R15, R27: an order names the rebalance that placed it; a person's order has none.
ALTER TABLE [Orders] ADD [RebalanceId] int NULL;

EXEC (N'
ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Rebalances_TenantId_RebalanceId]
    FOREIGN KEY ([TenantId], [RebalanceId]) REFERENCES [Rebalances] ([TenantId], [Id]);
CREATE INDEX [IX_Orders_RebalanceId] ON [Orders] ([RebalanceId]) WHERE [RebalanceId] IS NOT NULL;
');

-- R25: the outcome of a rebalance is on its Rebalances row. The three last-rebalance columns of the assignment are
-- dropped with no back-fill; rebalances before this script are in the ledger as Transactions with source Rebalance.
ALTER TABLE [AccountPortfolioAssignments] DROP CONSTRAINT [CK_AccountPortfolioAssignments_LastRebalanceStatus];
ALTER TABLE [AccountPortfolioAssignments] DROP COLUMN [LastRebalanceAt], [LastRebalanceStatus], [LastRebalanceMessage];
