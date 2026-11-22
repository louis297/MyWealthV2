-- Orders and settlement (docs/features/orders.md §6.1).
-- The broker connection a Tenant names; orders with their fills; the execution reports a broker connection's feed
-- delivers (platform data, no TenantId) and the state of that feed; the execution detail of a fill's Transaction;
-- source kind OrderFill (8), which always has a source id (the order's PublicId). Every existing Tenant is routed
-- to the simulator broker (roadmap D19). Forward only. No back-fill of Transactions: every Buy and Sell posted
-- before this script keeps its source and gets no execution detail, no order and no settlement.
-- The applicator runs this script without a surrounding transaction, so the column step only does what is missing.

IF COL_LENGTH(N'Tenants', N'BrokerConnection') IS NULL
BEGIN
    ALTER TABLE [Tenants] ADD [BrokerConnection] nvarchar(50) NULL;
    -- R3: every Tenant that exists now names the simulator. Dynamic, so the batch compiles before the column exists.
    EXEC(N'UPDATE [Tenants] SET [BrokerConnection] = N''simulator''');
END;

CREATE TABLE [ExecutionReports] (
    [Id] bigint NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [Connection] nvarchar(50) NOT NULL,
    [OutsideId] nvarchar(100) NOT NULL,
    [Kind] int NULL,
    [ClientOrderId] nvarchar(100) NULL,
    [BrokerOrderId] nvarchar(100) NULL,
    [Quantity] decimal(18, 8) NULL,
    [Price] decimal(18, 8) NULL,
    [TradeDate] date NULL,
    [SettlementDate] date NULL,
    [ContractNote] nvarchar(50) NULL,
    [FillOutsideId] nvarchar(100) NULL,
    [SettledDate] date NULL,
    [Reason] nvarchar(140) NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    [Status] int NOT NULL,
    [SkipReason] int NULL,
    [ProcessedAt] datetimeoffset NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ExecutionReports] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_ExecutionReports_PublicId] UNIQUE ([PublicId]),
    -- ExecutionReportKind: Fill, Cancelled, Expired, Settled; NULL for a kind the adapter did not know (R24).
    CONSTRAINT [CK_ExecutionReports_Kind] CHECK ([Kind] BETWEEN 0 AND 3),
    -- ExecutionReportStatus: Received, Processed, Skipped (R24, R25).
    CONSTRAINT [CK_ExecutionReports_Status] CHECK ([Status] BETWEEN 0 AND 2),
    -- ExecutionReportSkipReason: UnsupportedKind, UnknownOrder, UnknownFill, Overfill, Repeated; set iff Skipped.
    CONSTRAINT [CK_ExecutionReports_SkipReason] CHECK ([SkipReason] BETWEEN 0 AND 4),
    CONSTRAINT [CK_ExecutionReports_Skipped] CHECK (([Status] = 2 AND [SkipReason] IS NOT NULL) OR ([Status] <> 2 AND [SkipReason] IS NULL))
);

-- R24: a report is stored once per connection and outside id.
CREATE UNIQUE INDEX [UX_ExecutionReports_Connection_OutsideId] ON [ExecutionReports] ([Connection], [OutsideId]);
CREATE INDEX [IX_ExecutionReports_Connection_Status_ReceivedAt] ON [ExecutionReports] ([Connection], [Status], [ReceivedAt]);

-- R24: the cursor and the state of the execution report feed, one row per broker connection.
CREATE TABLE [BrokerFeeds] (
    [Connection] nvarchar(50) NOT NULL,
    [Cursor] nvarchar(500) NULL,
    [LastSucceededAt] datetimeoffset NULL,
    [LastFailedAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BrokerFeeds] PRIMARY KEY ([Connection]),
    -- BrokerFailure: Unreachable, Timeout, Unauthorized, Unavailable, BadResponse, InvalidItem.
    CONSTRAINT [CK_BrokerFeeds_LastFailure] CHECK ([LastFailure] BETWEEN 0 AND 5)
);

CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Side] int NOT NULL,
    [Type] int NOT NULL,
    [TimeInForce] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [Currency] char(3) NOT NULL,
    [ReferencePrice] decimal(18, 8) NOT NULL,
    [ReferencePriceDate] date NOT NULL,
    [PriceLimit] decimal(18, 8) NOT NULL,
    [TradeFeeScheduleId] int NULL,
    [Connection] nvarchar(50) NOT NULL,
    [Status] int NOT NULL,
    [FilledQuantity] decimal(18, 8) NOT NULL,
    [PostedGross] decimal(18, 4) NOT NULL,
    [FeeCharged] decimal(18, 4) NOT NULL,
    [ReservedCash] decimal(18, 4) NOT NULL,
    [ReservedQuantity] decimal(18, 8) NOT NULL,
    [Memo] nvarchar(200) NULL,
    [PlacedByUserId] int NOT NULL,
    [PlacedAt] datetimeoffset NOT NULL,
    [Attempts] int NOT NULL,
    [LastAttemptAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [AcceptedAt] datetimeoffset NULL,
    [BrokerOrderId] nvarchar(100) NULL,
    [RejectReason] int NULL,
    [CancelRequestedAt] datetimeoffset NULL,
    [CancelRequestedByUserId] int NULL,
    [CancelNote] nvarchar(200) NULL,
    [CancelAnsweredAt] datetimeoffset NULL,
    [ClosedAt] datetimeoffset NULL,
    [CloseReason] nvarchar(140) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Orders_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Orders_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_Orders_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Orders_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_Orders_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_Orders_TradeFeeSchedules_TenantId_TradeFeeScheduleId]
        FOREIGN KEY ([TenantId], [TradeFeeScheduleId]) REFERENCES [TradeFeeSchedules] ([TenantId], [Id]),
    CONSTRAINT [FK_Orders_Users_TenantId_PlacedByUserId]
        FOREIGN KEY ([TenantId], [PlacedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_Orders_Users_TenantId_CancelRequestedByUserId]
        FOREIGN KEY ([TenantId], [CancelRequestedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    -- OrderSide: Buy, Sell. OrderType: Market. TimeInForce: Day (R5).
    CONSTRAINT [CK_Orders_Side] CHECK ([Side] BETWEEN 0 AND 1),
    CONSTRAINT [CK_Orders_Type] CHECK ([Type] = 0),
    CONSTRAINT [CK_Orders_TimeInForce] CHECK ([TimeInForce] = 0),
    CONSTRAINT [CK_Orders_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_Orders_ReferencePrice] CHECK ([ReferencePrice] > 0),
    CONSTRAINT [CK_Orders_PriceLimit] CHECK ([PriceLimit] > 0),
    -- OrderStatus: Placed, Working, PartlyFilled, Filled, Cancelled, Rejected, Expired (R14).
    CONSTRAINT [CK_Orders_Status] CHECK ([Status] BETWEEN 0 AND 6),
    -- An order's filled quantity never exceeds its quantity.
    CONSTRAINT [CK_Orders_FilledQuantity] CHECK ([FilledQuantity] >= 0 AND [FilledQuantity] <= [Quantity]),
    CONSTRAINT [CK_Orders_ReservedCash] CHECK ([ReservedCash] >= 0),
    CONSTRAINT [CK_Orders_ReservedQuantity] CHECK ([ReservedQuantity] >= 0),
    -- BrokerFailure of a send: Unreachable, Timeout, Unauthorized, Unavailable, BadResponse (R21).
    CONSTRAINT [CK_Orders_LastFailure] CHECK ([LastFailure] BETWEEN 0 AND 4),
    -- OrderRejectReason: UnknownInstrument, InvalidOrder, MarketClosed, Refused; set iff Rejected.
    CONSTRAINT [CK_Orders_RejectReason] CHECK ([RejectReason] BETWEEN 0 AND 3),
    CONSTRAINT [CK_Orders_Rejected] CHECK (([Status] = 5 AND [RejectReason] IS NOT NULL) OR ([Status] <> 5 AND [RejectReason] IS NULL)),
    -- Closed iff Filled, Cancelled, Rejected or Expired.
    CONSTRAINT [CK_Orders_ClosedAt] CHECK (([Status] IN (3, 4, 5, 6) AND [ClosedAt] IS NOT NULL) OR ([Status] IN (0, 1, 2) AND [ClosedAt] IS NULL))
);

CREATE INDEX [IX_Orders_TenantId_PlacedAt] ON [Orders] ([TenantId], [PlacedAt] DESC);
CREATE INDEX [IX_Orders_AccountId_Status] ON [Orders] ([AccountId], [Status]);
-- R20, R22: the open orders of a connection, for the send and cancel steps.
CREATE INDEX [IX_Orders_Connection_Open] ON [Orders] ([Connection], [Id]) WHERE [Status] IN (0, 1, 2);

CREATE TABLE [OrderFills] (
    [Id] bigint NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [OrderId] int NOT NULL,
    [ExecutionReportId] bigint NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [Price] decimal(18, 8) NOT NULL,
    [TradeDate] date NOT NULL,
    [SettlementDate] date NOT NULL,
    [BrokerReference] nvarchar(50) NULL,
    [Status] int NOT NULL,
    [HoldReason] int NULL,
    [TransactionId] int NULL,
    [NetCash] decimal(18, 4) NULL,
    [PostedAt] datetimeoffset NULL,
    [SettledAt] datetimeoffset NULL,
    [SettledDate] date NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_OrderFills] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_OrderFills_PublicId] UNIQUE ([PublicId]),
    -- R26: a report is the source of at most one fill.
    CONSTRAINT [UQ_OrderFills_ExecutionReportId] UNIQUE ([ExecutionReportId]),
    CONSTRAINT [FK_OrderFills_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_OrderFills_Orders_TenantId_OrderId]
        FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
    CONSTRAINT [FK_OrderFills_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_OrderFills_ExecutionReports_ExecutionReportId]
        FOREIGN KEY ([ExecutionReportId]) REFERENCES [ExecutionReports] ([Id]),
    CONSTRAINT [CK_OrderFills_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_OrderFills_Price] CHECK ([Price] > 0),
    -- OrderFillStatus: Pending, Posted (R26).
    CONSTRAINT [CK_OrderFills_Status] CHECK ([Status] BETWEEN 0 AND 1),
    -- FillHoldReason: InsufficientCash, InsufficientHolding, AccountClosed, NoFxRate; only on a Pending fill (R36).
    CONSTRAINT [CK_OrderFills_HoldReason] CHECK ([HoldReason] BETWEEN 0 AND 3),
    CONSTRAINT [CK_OrderFills_Held] CHECK ([Status] = 0 OR [HoldReason] IS NULL),
    -- Posted iff it has its Transaction, its net cash and the time (R31).
    CONSTRAINT [CK_OrderFills_Posted] CHECK (
        ([Status] = 1 AND [TransactionId] IS NOT NULL AND [NetCash] IS NOT NULL AND [PostedAt] IS NOT NULL)
        OR ([Status] = 0 AND [TransactionId] IS NULL AND [NetCash] IS NULL AND [PostedAt] IS NULL)),
    -- Settled: the time and the broker's date, both or neither (R43).
    CONSTRAINT [CK_OrderFills_Settled] CHECK (
        ([SettledAt] IS NULL AND [SettledDate] IS NULL) OR ([SettledAt] IS NOT NULL AND [SettledDate] IS NOT NULL))
);

CREATE INDEX [IX_OrderFills_OrderId] ON [OrderFills] ([OrderId]);
-- R36: the fills that wait to be posted.
CREATE INDEX [IX_OrderFills_Pending] ON [OrderFills] ([OrderId]) WHERE [Status] = 0;
-- R44: the posted fills that have not settled, for unsettled cash.
CREATE INDEX [IX_OrderFills_Unsettled] ON [OrderFills] ([TenantId], [OrderId]) WHERE [Status] = 1 AND [SettledAt] IS NULL;

-- R35: the execution detail of a fill's Transaction and of its Reversal. Part of an immutable Transaction: no
-- PublicId and no RowVersion.
CREATE TABLE [TransactionExecutionDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [FillPublicId] uniqueidentifier NOT NULL,
    [Price] decimal(18, 8) NOT NULL,
    [TradeDate] date NOT NULL,
    [SettlementDate] date NOT NULL,
    [BrokerReference] nvarchar(50) NULL,
    CONSTRAINT [PK_TransactionExecutionDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionExecutionDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionExecutionDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_TransactionExecutionDetails_Price] CHECK ([Price] > 0),
    CONSTRAINT [CK_TransactionExecutionDetails_Dates] CHECK ([SettlementDate] >= [TradeDate])
);

CREATE INDEX [IX_TransactionExecutionDetails_TenantId] ON [TransactionExecutionDetails] ([TenantId]);

-- Transaction source kind OrderFill = 8, with the order's PublicId as source id.
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 8);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5, 6, 7, 8) AND [SourcePublicId] IS NOT NULL));
