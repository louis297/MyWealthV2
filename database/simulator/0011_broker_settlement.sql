-- The simulator broker's settlements and broken fills (docs/features/trade-settlement.md §6.2, R35–R42).
-- An order may name the trust account it settles against. When its fills settle, the broker nets them per settlement
-- account, currency and settlement date into one broker settlement, and closes it with a Settlement report that names
-- no order. The settlement mode says whether the broker pays its receipts at once or holds them. A broken fill never
-- settles and is in no settlement. A reset empties the settlements and sets the mode back to Pay (R42).
-- The applicator runs this script without a surrounding transaction. Statements that name a column added here run
-- inside EXEC, so each batch compiles before the column exists.

-- R38: Pay (0) or Hold (1).
ALTER TABLE [BrokerSettings] ADD [SettlementMode] int NOT NULL CONSTRAINT [DF_BrokerSettings_SettlementMode] DEFAULT (0);
EXEC(N'ALTER TABLE [BrokerSettings] ADD CONSTRAINT [CK_BrokerSettings_SettlementMode] CHECK ([SettlementMode] BETWEEN 0 AND 1);');

-- R35: the trust account number, stored as text; the broker does not read the platform.
ALTER TABLE [BrokerOrders] ADD [SettlementAccount] nvarchar(34) NULL;

CREATE TABLE [BrokerSettlements] (
    [Id] int NOT NULL IDENTITY,
    [Number] int NOT NULL,
    [SettlementAccount] nvarchar(34) NOT NULL,
    [Currency] char(3) NOT NULL,
    [SettlementDate] date NOT NULL,
    [NetAmount] decimal(18, 4) NOT NULL,
    [FillCount] int NOT NULL,
    [PaidAmount] decimal(18, 4) NOT NULL,
    CONSTRAINT [PK_BrokerSettlements] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BrokerSettlements_FillCount] CHECK ([FillCount] >= 1),
    CONSTRAINT [CK_BrokerSettlements_PaidAmount] CHECK ([PaidAmount] >= 0)
);

-- R36: n counts the settlements of the world from 1.
CREATE UNIQUE INDEX [UX_BrokerSettlements_Number] ON [BrokerSettlements] ([Number]);

-- R36, R40: the settlement a fill settled in, and a broken fill.
ALTER TABLE [BrokerFills] ADD
    [Broken] bit NOT NULL CONSTRAINT [DF_BrokerFills_Broken] DEFAULT (0),
    [SettlementId] int NULL;
EXEC(N'
ALTER TABLE [BrokerFills] ADD CONSTRAINT [FK_BrokerFills_BrokerSettlements_SettlementId]
    FOREIGN KEY ([SettlementId]) REFERENCES [BrokerSettlements] ([Id]);
-- A broken fill never settles and is in no settlement.
ALTER TABLE [BrokerFills] ADD CONSTRAINT [CK_BrokerFills_Broken] CHECK ([Broken] = 0 OR ([Settled] = 0 AND [SettlementId] IS NULL));
');

-- R36, R40, R41: a Settled report may name its settlement; a Settlement report names a settlement and no order; a
-- Broken report names its fill.
ALTER TABLE [BrokerReports] ADD [SettlementId] int NULL;
ALTER TABLE [BrokerReports] DROP CONSTRAINT [FK_BrokerReports_BrokerOrders_OrderId];
DROP INDEX [IX_BrokerReports_OrderId] ON [BrokerReports];
ALTER TABLE [BrokerReports] ALTER COLUMN [OrderId] int NULL;
ALTER TABLE [BrokerReports] ADD CONSTRAINT [FK_BrokerReports_BrokerOrders_OrderId]
    FOREIGN KEY ([OrderId]) REFERENCES [BrokerOrders] ([Id]);
CREATE INDEX [IX_BrokerReports_OrderId] ON [BrokerReports] ([OrderId]);
ALTER TABLE [BrokerReports] DROP CONSTRAINT [CK_BrokerReports_Kind];
ALTER TABLE [BrokerReports] DROP CONSTRAINT [CK_BrokerReports_Fill];
ALTER TABLE [BrokerReports] DROP CONSTRAINT [CK_BrokerReports_Reason];
-- Fill, Cancelled, Expired, Settled, Broken, Settlement.
ALTER TABLE [BrokerReports] ADD CONSTRAINT [CK_BrokerReports_Kind] CHECK ([Kind] BETWEEN 0 AND 5);
ALTER TABLE [BrokerReports] ADD CONSTRAINT [CK_BrokerReports_Fill] CHECK (
    ([Kind] IN (0, 3, 4) AND [FillId] IS NOT NULL) OR ([Kind] IN (1, 2, 5) AND [FillId] IS NULL));
ALTER TABLE [BrokerReports] ADD CONSTRAINT [CK_BrokerReports_Reason] CHECK ([Kind] IN (1, 4) OR [Reason] IS NULL);
EXEC(N'
ALTER TABLE [BrokerReports] ADD CONSTRAINT [FK_BrokerReports_BrokerSettlements_SettlementId]
    FOREIGN KEY ([SettlementId]) REFERENCES [BrokerSettlements] ([Id]);
ALTER TABLE [BrokerReports] ADD CONSTRAINT [CK_BrokerReports_Order] CHECK (
    ([Kind] = 5 AND [OrderId] IS NULL AND [SettlementId] IS NOT NULL)
    OR ([Kind] <> 5 AND [OrderId] IS NOT NULL));
ALTER TABLE [BrokerReports] ADD CONSTRAINT [CK_BrokerReports_Settlement] CHECK ([Kind] IN (3, 5) OR [SettlementId] IS NULL);
');
