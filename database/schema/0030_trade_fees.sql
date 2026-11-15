-- Trade fees (docs/features/trade-fees.md §6).
-- Cash legs gain a role (Principal = 0, Fee = 1); existing legs become Principal from the default.
-- Adds tenant trade fee schedules with marginal bands, and one fee detail per Fee cash leg.

ALTER TABLE [TransactionCashLegs] ADD [Role] int NOT NULL
    CONSTRAINT [DF_TransactionCashLegs_Role] DEFAULT (0);

EXEC(N'
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [CK_TransactionCashLegs_Role] CHECK ([Role] BETWEEN 0 AND 1);
ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [UQ_TransactionCashLegs_TransactionId_AccountId];
CREATE UNIQUE INDEX [IX_TransactionCashLegs_TransactionId_AccountId_Principal]
    ON [TransactionCashLegs] ([TransactionId], [AccountId])
    WHERE [Role] = 0;
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [UQ_TransactionCashLegs_TenantId_Id] UNIQUE ([TenantId], [Id]);
');

CREATE TABLE [TradeFeeSchedules] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [QuoteCurrency] char(3) NOT NULL,
    [EffectiveFrom] date NOT NULL,
    [MinimumAmount] decimal(18, 4) NOT NULL,
    [CapAmount] decimal(18, 4) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_TradeFeeSchedules] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_TradeFeeSchedules_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_TradeFeeSchedules_TenantId_QuoteCurrency_EffectiveFrom]
        UNIQUE ([TenantId], [QuoteCurrency], [EffectiveFrom]),
    CONSTRAINT [UQ_TradeFeeSchedules_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_TradeFeeSchedules_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TradeFeeSchedules_Currencies_QuoteCurrency]
        FOREIGN KEY ([QuoteCurrency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_TradeFeeSchedules_MinimumAmount] CHECK ([MinimumAmount] >= 0),
    CONSTRAINT [CK_TradeFeeSchedules_CapAmount]
        CHECK ([CapAmount] IS NULL OR ([CapAmount] > 0 AND [CapAmount] >= [MinimumAmount]))
);

CREATE TABLE [TradeFeeBands] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [TradeFeeScheduleId] int NOT NULL,
    [FromAmount] decimal(18, 4) NOT NULL,
    [RatePercent] decimal(7, 4) NOT NULL,
    CONSTRAINT [PK_TradeFeeBands] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_TradeFeeBands_TradeFeeScheduleId_FromAmount] UNIQUE ([TradeFeeScheduleId], [FromAmount]),
    CONSTRAINT [FK_TradeFeeBands_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TradeFeeBands_TradeFeeSchedules_TenantId_TradeFeeScheduleId]
        FOREIGN KEY ([TenantId], [TradeFeeScheduleId]) REFERENCES [TradeFeeSchedules] ([TenantId], [Id]),
    CONSTRAINT [CK_TradeFeeBands_FromAmount] CHECK ([FromAmount] >= 0),
    CONSTRAINT [CK_TradeFeeBands_RatePercent] CHECK ([RatePercent] >= 0 AND [RatePercent] <= 100)
);

CREATE TABLE [TransactionFeeDetails] (
    [CashLegId] int NOT NULL,
    [TenantId] int NOT NULL,
    [TransactionId] int NOT NULL,
    [Kind] int NOT NULL,
    [TradeFeeScheduleId] int NULL,
    [BasisAmount] decimal(18, 4) NOT NULL,
    [FeeAmount] decimal(18, 4) NOT NULL,
    [Currency] char(3) NOT NULL,
    CONSTRAINT [PK_TransactionFeeDetails] PRIMARY KEY ([CashLegId]),
    CONSTRAINT [UQ_TransactionFeeDetails_TransactionId_Kind] UNIQUE ([TransactionId], [Kind]),
    CONSTRAINT [FK_TransactionFeeDetails_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionFeeDetails_TransactionCashLegs_TenantId_CashLegId]
        FOREIGN KEY ([TenantId], [CashLegId]) REFERENCES [TransactionCashLegs] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionFeeDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionFeeDetails_TradeFeeSchedules_TenantId_TradeFeeScheduleId]
        FOREIGN KEY ([TenantId], [TradeFeeScheduleId]) REFERENCES [TradeFeeSchedules] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionFeeDetails_Currencies_Currency]
        FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_TransactionFeeDetails_Kind] CHECK ([Kind] BETWEEN 0 AND 0),
    CONSTRAINT [CK_TransactionFeeDetails_BrokerageSchedule] CHECK ([Kind] <> 0 OR [TradeFeeScheduleId] IS NOT NULL),
    CONSTRAINT [CK_TransactionFeeDetails_BasisAmount] CHECK ([BasisAmount] > 0),
    CONSTRAINT [CK_TransactionFeeDetails_FeeAmount] CHECK ([FeeAmount] > 0)
);

CREATE INDEX [IX_TradeFeeBands_TenantId] ON [TradeFeeBands] ([TenantId]);
CREATE INDEX [IX_TransactionFeeDetails_TenantId] ON [TransactionFeeDetails] ([TenantId]);
CREATE INDEX [IX_TransactionFeeDetails_TradeFeeScheduleId] ON [TransactionFeeDetails] ([TradeFeeScheduleId]);
