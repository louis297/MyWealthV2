-- The simulator broker (docs/features/orders.md §6.2, R52–R65).
-- The broker takes orders for any symbol and plays the exchange too. It keeps its orders, their fills and the reports
-- it sends in its own tables, and holds no positions and no cash. BrokerSettings is one row: how new orders are
-- answered, the slippage of a fill price and the days to settlement. A report's Id is its place in the feed. A reset
-- empties the three tables and sets the settings back (R65).

CREATE TABLE [BrokerSettings] (
    [Id] int NOT NULL,
    [Mode] int NOT NULL,
    [FillPercent] int NOT NULL,
    [RejectReason] int NULL,
    [SlippagePercent] decimal(5, 2) NOT NULL,
    [SettlementDays] int NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BrokerSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BrokerSettings_SingleRow] CHECK ([Id] = 1),
    -- Fill, Hold, Reject.
    CONSTRAINT [CK_BrokerSettings_Mode] CHECK ([Mode] BETWEEN 0 AND 2),
    CONSTRAINT [CK_BrokerSettings_FillPercent] CHECK ([FillPercent] BETWEEN 1 AND 100),
    -- UnknownInstrument, InvalidOrder, MarketClosed, Refused; set with Reject only.
    CONSTRAINT [CK_BrokerSettings_RejectReason] CHECK (
        ([Mode] = 2 AND [RejectReason] BETWEEN 0 AND 3) OR ([Mode] <> 2 AND [RejectReason] IS NULL)),
    CONSTRAINT [CK_BrokerSettings_SlippagePercent] CHECK ([SlippagePercent] BETWEEN 0 AND 5),
    CONSTRAINT [CK_BrokerSettings_SettlementDays] CHECK ([SettlementDays] BETWEEN 0 AND 5)
);

-- R55, R56, R61: Fill at 100, slippage 0.10, two days to settlement.
INSERT INTO [BrokerSettings] ([Id], [Mode], [FillPercent], [RejectReason], [SlippagePercent], [SettlementDays])
VALUES (1, 0, 100, NULL, 0.10, 2);

CREATE TABLE [BrokerOrders] (
    [Id] int NOT NULL IDENTITY,
    [Number] int NOT NULL,
    [ClientOrderId] nvarchar(100) NOT NULL,
    [BodyHash] varbinary(32) NOT NULL,
    [Symbol] nvarchar(32) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Side] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [PriceLimit] decimal(18, 8) NOT NULL,
    [ReferencePrice] decimal(18, 8) NOT NULL,
    [Status] int NOT NULL,
    [RejectReason] int NULL,
    [FilledQuantity] decimal(18, 8) NOT NULL,
    [AcceptedDay] date NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BrokerOrders] PRIMARY KEY ([Id]),
    -- Buy, Sell.
    CONSTRAINT [CK_BrokerOrders_Side] CHECK ([Side] BETWEEN 0 AND 1),
    CONSTRAINT [CK_BrokerOrders_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_BrokerOrders_Prices] CHECK ([PriceLimit] > 0 AND [ReferencePrice] > 0),
    -- Working, Filled, Cancelled, Expired, Rejected.
    CONSTRAINT [CK_BrokerOrders_Status] CHECK ([Status] BETWEEN 0 AND 4),
    -- A rejected order has a reason and was never accepted; every other order was accepted on a simulator day.
    CONSTRAINT [CK_BrokerOrders_Rejected] CHECK (
        ([Status] = 4 AND [RejectReason] BETWEEN 0 AND 3 AND [AcceptedDay] IS NULL)
        OR ([Status] <> 4 AND [RejectReason] IS NULL AND [AcceptedDay] IS NOT NULL)),
    CONSTRAINT [CK_BrokerOrders_FilledQuantity] CHECK ([FilledQuantity] >= 0 AND [FilledQuantity] <= [Quantity])
);

-- R54: n counts the orders of the world from 1.
CREATE UNIQUE INDEX [UX_BrokerOrders_Number] ON [BrokerOrders] ([Number]);

-- R54: one order per client order id.
CREATE UNIQUE INDEX [UX_BrokerOrders_ClientOrderId] ON [BrokerOrders] ([ClientOrderId]);

CREATE TABLE [BrokerFills] (
    [Id] int NOT NULL IDENTITY,
    [Number] int NOT NULL,
    [OrderId] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [Price] decimal(18, 4) NOT NULL,
    [TradeDate] date NOT NULL,
    [SettlementDate] date NOT NULL,
    [ContractNote] nvarchar(50) NOT NULL,
    [Settled] bit NOT NULL,
    CONSTRAINT [PK_BrokerFills] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BrokerFills_BrokerOrders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [BrokerOrders] ([Id]),
    CONSTRAINT [CK_BrokerFills_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_BrokerFills_Price] CHECK ([Price] > 0),
    CONSTRAINT [CK_BrokerFills_SettlementDate] CHECK ([SettlementDate] >= [TradeDate])
);

-- R56, R57: n counts the fills of the world from 1.
CREATE UNIQUE INDEX [UX_BrokerFills_Number] ON [BrokerFills] ([Number]);

CREATE INDEX [IX_BrokerFills_OrderId] ON [BrokerFills] ([OrderId]);

CREATE TABLE [BrokerReports] (
    [Id] bigint NOT NULL IDENTITY,
    [Kind] int NOT NULL,
    [OrderId] int NOT NULL,
    [FillId] int NULL,
    [SettledDate] date NULL,
    [Reason] nvarchar(140) NULL,
    CONSTRAINT [PK_BrokerReports] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BrokerReports_BrokerOrders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [BrokerOrders] ([Id]),
    CONSTRAINT [FK_BrokerReports_BrokerFills_FillId] FOREIGN KEY ([FillId]) REFERENCES [BrokerFills] ([Id]),
    -- Fill, Cancelled, Expired, Settled.
    CONSTRAINT [CK_BrokerReports_Kind] CHECK ([Kind] BETWEEN 0 AND 3),
    -- A Fill and a Settled report name a fill; a Settled report says when; only a Cancelled report has a reason.
    CONSTRAINT [CK_BrokerReports_Fill] CHECK (([Kind] IN (0, 3) AND [FillId] IS NOT NULL) OR ([Kind] IN (1, 2) AND [FillId] IS NULL)),
    CONSTRAINT [CK_BrokerReports_SettledDate] CHECK (([Kind] = 3 AND [SettledDate] IS NOT NULL) OR ([Kind] <> 3 AND [SettledDate] IS NULL)),
    CONSTRAINT [CK_BrokerReports_Reason] CHECK ([Kind] = 1 OR [Reason] IS NULL)
);

CREATE INDEX [IX_BrokerReports_OrderId] ON [BrokerReports] ([OrderId]);
