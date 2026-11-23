-- Trade settlement cash and broken trades (docs/features/trade-settlement.md §6.1).
-- The platform's record of each broker settlement per client money account; the settlement account an order was
-- placed with; the broker settlement id, the trade settlement and the break of a fill; the settlement fields of an
-- execution report and its two new kinds; outgoing payments of kind TradeSettlement; statement lines matched to a
-- trade settlement; four reconciliation item kinds and the open settlements of a run. Forward only, no back-fill:
-- existing orders have no settlement account, existing fills no settlement id and no break, existing payments keep
-- their kind and stored reconciliation runs keep their items.
-- The applicator runs this script without a surrounding transaction. Statements that name a column added here run
-- inside EXEC, so each batch compiles before the column exists.

-- The composite key the tenant FKs below need.
ALTER TABLE [ClientMoneyAccounts] ADD CONSTRAINT [UQ_ClientMoneyAccounts_TenantId_Id] UNIQUE ([TenantId], [Id]);

CREATE TABLE [TradeSettlements] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [ClientMoneyAccountId] int NOT NULL,
    [Connection] nvarchar(50) NOT NULL,
    [BrokerSettlementId] nvarchar(100) NOT NULL,
    [Currency] char(3) NOT NULL,
    [SettledDate] date NOT NULL,
    [FillCount] int NOT NULL,
    [NetAmount] decimal(18, 4) NOT NULL,
    [BrokerFillCount] int NOT NULL,
    [BrokerNetAmount] decimal(18, 4) NOT NULL,
    [BrokerSettlementAccount] nvarchar(34) NOT NULL,
    [ExecutionReportId] bigint NOT NULL,
    [Status] int NOT NULL,
    [Failure] int NULL,
    [FailureDetail] nvarchar(140) NULL,
    [CompletedAt] datetimeoffset NULL,
    [SetAsideByUserId] int NULL,
    [SetAsideAt] datetimeoffset NULL,
    [SetAsideNote] nvarchar(200) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_TradeSettlements] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_TradeSettlements_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_TradeSettlements_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_TradeSettlements_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TradeSettlements_ClientMoneyAccounts_TenantId_ClientMoneyAccountId]
        FOREIGN KEY ([TenantId], [ClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([TenantId], [Id]),
    CONSTRAINT [FK_TradeSettlements_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_TradeSettlements_ExecutionReports_ExecutionReportId]
        FOREIGN KEY ([ExecutionReportId]) REFERENCES [ExecutionReports] ([Id]),
    CONSTRAINT [FK_TradeSettlements_Users_TenantId_SetAsideByUserId]
        FOREIGN KEY ([TenantId], [SetAsideByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_TradeSettlements_FillCount] CHECK ([FillCount] >= 1 AND [BrokerFillCount] >= 1),
    -- TradeSettlementStatus: AwaitingPayment, AwaitingReceipt, Completed, PaymentFailed, Disputed, SetAside (R10).
    CONSTRAINT [CK_TradeSettlements_Status] CHECK ([Status] BETWEEN 0 AND 5),
    -- TradeSettlementFailure: PaymentRejected, PaymentReturned, NoBrokerAccount; set iff PaymentFailed.
    CONSTRAINT [CK_TradeSettlements_Failure] CHECK ([Failure] BETWEEN 0 AND 2),
    CONSTRAINT [CK_TradeSettlements_Failed] CHECK (
        ([Status] = 3 AND [Failure] IS NOT NULL) OR ([Status] <> 3 AND [Failure] IS NULL AND [FailureDetail] IS NULL)),
    CONSTRAINT [CK_TradeSettlements_Completed] CHECK (
        ([Status] = 2 AND [CompletedAt] IS NOT NULL) OR ([Status] <> 2 AND [CompletedAt] IS NULL)),
    -- R15: the person, the time and the note, all set iff SetAside.
    CONSTRAINT [CK_TradeSettlements_SetAside] CHECK (
        ([Status] = 5 AND [SetAsideByUserId] IS NOT NULL AND [SetAsideAt] IS NOT NULL AND [SetAsideNote] IS NOT NULL)
        OR ([Status] <> 5 AND [SetAsideByUserId] IS NULL AND [SetAsideAt] IS NULL AND [SetAsideNote] IS NULL))
);

-- R8: one trade settlement per connection, broker settlement id and client money account.
CREATE UNIQUE INDEX [UX_TradeSettlements_Connection_BrokerSettlementId_ClientMoneyAccountId]
    ON [TradeSettlements] ([Connection], [BrokerSettlementId], [ClientMoneyAccountId]);
CREATE INDEX [IX_TradeSettlements_TenantId_Status_Id] ON [TradeSettlements] ([TenantId], [Status], [Id] DESC);

-- R3: the settlement account of an order, both or neither.
ALTER TABLE [Orders] ADD [ClientMoneyAccountId] int NULL, [SettlementAccountNumber] nvarchar(34) NULL;
EXEC(N'
ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_ClientMoneyAccounts_TenantId_ClientMoneyAccountId]
    FOREIGN KEY ([TenantId], [ClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([TenantId], [Id]);
ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_SettlementAccount] CHECK (
    ([ClientMoneyAccountId] IS NULL AND [SettlementAccountNumber] IS NULL)
    OR ([ClientMoneyAccountId] IS NOT NULL AND [SettlementAccountNumber] IS NOT NULL));
');

-- R6, R8, R26–R31: the broker settlement and the trade settlement of a fill, and its break.
ALTER TABLE [OrderFills] ADD
    [BrokerSettlementId] nvarchar(100) NULL,
    [TradeSettlementId] int NULL,
    [BrokenAt] datetimeoffset NULL,
    [BreakReportId] bigint NULL,
    [BreakReason] nvarchar(140) NULL,
    [BreakOutcome] int NULL,
    [BreakHoldReason] int NULL,
    [ReversalTransactionId] int NULL,
    [BreakResolvedByUserId] int NULL,
    [BreakResolvedAt] datetimeoffset NULL,
    [BreakNote] nvarchar(200) NULL;
ALTER TABLE [OrderFills] DROP CONSTRAINT [CK_OrderFills_Status];
ALTER TABLE [OrderFills] DROP CONSTRAINT [CK_OrderFills_Posted];
-- OrderFillStatus: Pending, Posted, Broken.
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_Status] CHECK ([Status] BETWEEN 0 AND 2);
-- A Posted fill has its Transaction, net cash and time; a Pending one none; a Broken one keeps what it had.
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_Posted] CHECK (
    ([Status] = 1 AND [TransactionId] IS NOT NULL AND [NetCash] IS NOT NULL AND [PostedAt] IS NOT NULL)
    OR ([Status] = 0 AND [TransactionId] IS NULL AND [NetCash] IS NULL AND [PostedAt] IS NULL)
    OR [Status] = 2);
EXEC(N'
ALTER TABLE [OrderFills] ADD CONSTRAINT [FK_OrderFills_TradeSettlements_TenantId_TradeSettlementId]
    FOREIGN KEY ([TenantId], [TradeSettlementId]) REFERENCES [TradeSettlements] ([TenantId], [Id]);
ALTER TABLE [OrderFills] ADD CONSTRAINT [FK_OrderFills_Transactions_TenantId_ReversalTransactionId]
    FOREIGN KEY ([TenantId], [ReversalTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]);
ALTER TABLE [OrderFills] ADD CONSTRAINT [FK_OrderFills_ExecutionReports_BreakReportId]
    FOREIGN KEY ([BreakReportId]) REFERENCES [ExecutionReports] ([Id]);
ALTER TABLE [OrderFills] ADD CONSTRAINT [FK_OrderFills_Users_TenantId_BreakResolvedByUserId]
    FOREIGN KEY ([TenantId], [BreakResolvedByUserId]) REFERENCES [Users] ([TenantId], [Id]);
-- FillBreakOutcome: Reversed, NotPosted, NotApplied, ResolvedByHand. FillBreakHoldReason: AccountClosed,
-- LaterActivity, InsufficientCash.
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_BreakOutcome] CHECK ([BreakOutcome] BETWEEN 0 AND 3);
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_BreakHoldReason] CHECK ([BreakHoldReason] BETWEEN 0 AND 2);
-- The time, the report and the outcome of a break, set iff Broken.
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_Broken] CHECK (
    ([Status] = 2 AND [BrokenAt] IS NOT NULL AND [BreakReportId] IS NOT NULL AND [BreakOutcome] IS NOT NULL)
    OR ([Status] <> 2 AND [BrokenAt] IS NULL AND [BreakReportId] IS NULL AND [BreakOutcome] IS NULL
        AND [BreakReason] IS NULL));
-- R30: a hold reason iff the break could not be applied (and stays when it is resolved by hand).
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_BreakHeld] CHECK (
    (ISNULL([BreakOutcome], -1) IN (2, 3) AND [BreakHoldReason] IS NOT NULL)
    OR (ISNULL([BreakOutcome], -1) NOT IN (2, 3) AND [BreakHoldReason] IS NULL));
-- R29: the Reversal iff Reversed.
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_Reversal] CHECK (
    (ISNULL([BreakOutcome], -1) = 0 AND [ReversalTransactionId] IS NOT NULL)
    OR (ISNULL([BreakOutcome], -1) <> 0 AND [ReversalTransactionId] IS NULL));
-- R31: the person, the time and the note iff ResolvedByHand.
ALTER TABLE [OrderFills] ADD CONSTRAINT [CK_OrderFills_BreakResolved] CHECK (
    (ISNULL([BreakOutcome], -1) = 3 AND [BreakResolvedByUserId] IS NOT NULL AND [BreakResolvedAt] IS NOT NULL
        AND [BreakNote] IS NOT NULL)
    OR (ISNULL([BreakOutcome], -1) <> 3 AND [BreakResolvedByUserId] IS NULL AND [BreakResolvedAt] IS NULL
        AND [BreakNote] IS NULL));
-- A report breaks at most one fill.
CREATE UNIQUE INDEX [UX_OrderFills_BreakReportId] ON [OrderFills] ([BreakReportId]) WHERE [BreakReportId] IS NOT NULL;
CREATE INDEX [IX_OrderFills_TradeSettlementId] ON [OrderFills] ([TradeSettlementId]);
-- R8: the fills of a broker settlement that are in no trade settlement yet.
CREATE INDEX [IX_OrderFills_BrokerSettlementId_Unassigned] ON [OrderFills] ([BrokerSettlementId])
    WHERE [TradeSettlementId] IS NULL AND [BrokerSettlementId] IS NOT NULL;
');

-- R6, R7, R26: the settlement fields of a report; kinds Broken and Settlement; three skip reasons.
ALTER TABLE [ExecutionReports] ADD
    [SettlementId] nvarchar(100) NULL,
    [SettlementAccount] nvarchar(34) NULL,
    [Currency] char(3) NULL,
    [NetAmount] decimal(18, 4) NULL,
    [FillCount] int NULL;
ALTER TABLE [ExecutionReports] DROP CONSTRAINT [CK_ExecutionReports_Kind];
ALTER TABLE [ExecutionReports] DROP CONSTRAINT [CK_ExecutionReports_SkipReason];
-- ExecutionReportKind: Fill, Cancelled, Expired, Settled, Broken, Settlement.
ALTER TABLE [ExecutionReports] ADD CONSTRAINT [CK_ExecutionReports_Kind] CHECK ([Kind] BETWEEN 0 AND 5);
-- ExecutionReportSkipReason: ... Repeated, FillSettled, FillBroken, UnknownSettlement.
ALTER TABLE [ExecutionReports] ADD CONSTRAINT [CK_ExecutionReports_SkipReason] CHECK ([SkipReason] BETWEEN 0 AND 7);

-- R12: an outgoing payment of kind TradeSettlement names its trade settlement; one open payment per settlement.
ALTER TABLE [OutgoingPayments] ADD [TradeSettlementId] int NULL;
ALTER TABLE [OutgoingPayments] DROP CONSTRAINT [CK_OutgoingPayments_Kind];
ALTER TABLE [OutgoingPayments] DROP CONSTRAINT [CK_OutgoingPayments_Target];
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [CK_OutgoingPayments_Kind] CHECK ([Kind] BETWEEN 0 AND 2);
EXEC(N'
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [CK_OutgoingPayments_Target] CHECK (
    ([Kind] = 0 AND [WithdrawalId] IS NOT NULL AND [ReturnOfLineId] IS NULL AND [TradeSettlementId] IS NULL)
    OR ([Kind] = 1 AND [WithdrawalId] IS NULL AND [ReturnOfLineId] IS NOT NULL AND [TradeSettlementId] IS NULL)
    OR ([Kind] = 2 AND [WithdrawalId] IS NULL AND [ReturnOfLineId] IS NULL AND [TradeSettlementId] IS NOT NULL));
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [FK_OutgoingPayments_TradeSettlements_TenantId_TradeSettlementId]
    FOREIGN KEY ([TenantId], [TradeSettlementId]) REFERENCES [TradeSettlements] ([TenantId], [Id]);
CREATE UNIQUE INDEX [UX_OutgoingPayments_TradeSettlementId_Open]
    ON [OutgoingPayments] ([TradeSettlementId]) WHERE [Kind] = 2 AND [Status] IN (0, 1);
');

-- R18–R21: a statement line matched to a trade settlement; method Settlement (5), reason SettlementMismatch (13).
ALTER TABLE [BankStatementLines] ADD [TradeSettlementId] int NULL;
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_UnmatchedReason];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_MatchMethod];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_Match];
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_UnmatchedReason] CHECK ([UnmatchedReason] BETWEEN 0 AND 13);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_MatchMethod] CHECK ([MatchMethod] BETWEEN 0 AND 5);
EXEC(N'
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [FK_BankStatementLines_TradeSettlements_TenantId_TradeSettlementId]
    FOREIGN KEY ([TenantId], [TradeSettlementId]) REFERENCES [TradeSettlements] ([TenantId], [Id]);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_Match] CHECK (
    ([Status] = 1 AND [MatchMethod] IS NOT NULL AND [MatchedAt] IS NOT NULL
        AND (([MatchedAccountId] IS NOT NULL AND [MatchedTransactionId] IS NOT NULL AND [TradeSettlementId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [RelatedLineId] IS NOT NULL
                AND [TradeSettlementId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [TradeSettlementId] IS NOT NULL)))
    OR ([Status] <> 1 AND [MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL
        AND [MatchMethod] IS NULL AND [MatchedAt] IS NULL AND [TradeSettlementId] IS NULL));
CREATE INDEX [IX_BankStatementLines_TradeSettlementId] ON [BankStatementLines] ([TradeSettlementId])
    WHERE [TradeSettlementId] IS NOT NULL;
');

-- R25: UnsettledTrades (15), TradesNeedingAction (16), UnbookedFills (17), TradeFees (18); none carries a line.
ALTER TABLE [BankReconciliationItems] DROP CONSTRAINT [CK_BankReconciliationItems_Kind];
ALTER TABLE [BankReconciliationItems] ADD CONSTRAINT [CK_BankReconciliationItems_Kind] CHECK ([Kind] BETWEEN 0 AND 18);

-- R25: the open trade settlements of the run's account, recorded without an item.
ALTER TABLE [BankReconciliations] ADD
    [OpenSettlementsCount] int NOT NULL CONSTRAINT [DF_BankReconciliations_OpenSettlementsCount] DEFAULT (0),
    [OpenSettlementsAmount] decimal(18, 4) NOT NULL CONSTRAINT [DF_BankReconciliations_OpenSettlementsAmount] DEFAULT (0);
