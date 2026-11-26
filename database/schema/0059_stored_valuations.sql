-- Valuation run (docs/features/valuation-run.md §6.1).
-- A stored valuation is the account-valuation result of one Custodial account and one UTC day, with one line per
-- holding that keeps the price row and the rate row it used. It is stored once and never changed (R7, R10).
-- A valuation run is one stored execution of that work for one Tenant and one through date (R13).
-- Forward only, no back-fill: ValuedThrough starts null everywhere and the first run values the past (R16).
-- The applicator runs this script without a surrounding transaction.

CREATE TABLE [ValuationRuns] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [ThroughDate] date NOT NULL,
    [Trigger] int NOT NULL,
    [RanByUserId] int NULL,
    [StartedAt] datetimeoffset NOT NULL,
    [CompletedAt] datetimeoffset NULL,
    [AccountsProcessed] int NOT NULL,
    [AccountsFailed] int NOT NULL,
    [DaysValued] int NOT NULL,
    [DaysIncomplete] int NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_ValuationRuns] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_ValuationRuns_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_ValuationRuns_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_ValuationRuns_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    -- A SystemAdmin has no tenant, so the person is not a tenant composite key.
    CONSTRAINT [FK_ValuationRuns_Users_RanByUserId] FOREIGN KEY ([RanByUserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [CK_ValuationRuns_Trigger] CHECK ([Trigger] BETWEEN 0 AND 1),
    -- R13: no person for the daily job, a person for a manual run.
    CONSTRAINT [CK_ValuationRuns_RanBy] CHECK (([Trigger] = 0 AND [RanByUserId] IS NULL) OR ([Trigger] <> 0 AND [RanByUserId] IS NOT NULL)),
    CONSTRAINT [CK_ValuationRuns_Counts] CHECK ([AccountsProcessed] >= 0 AND [AccountsFailed] >= 0 AND [DaysValued] >= 0
        AND [DaysIncomplete] >= 0 AND [DaysIncomplete] <= [DaysValued])
);

CREATE INDEX [IX_ValuationRuns_TenantId_StartedAt] ON [ValuationRuns] ([TenantId], [StartedAt] DESC);

-- R5: the last UTC day a stored valuation exists for; null before the first, and never on an External account.
ALTER TABLE [Accounts] ADD [ValuedThrough] date NULL;

-- The script is one batch, so the statement that names the new column runs through EXEC.
EXEC(N'
ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Accounts_ValuedThrough] CHECK ([ValuedThrough] IS NULL OR [CustodyMode] = 1);
');

-- R8: one row per Custodial account and day, as ValueAccount returned it.
CREATE TABLE [StoredValuations] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [ValuationDate] date NOT NULL,
    [Currency] char(3) NOT NULL,
    [Cash] decimal(18, 4) NOT NULL,
    [HoldingsValue] decimal(18, 4) NOT NULL,
    [Total] decimal(18, 4) NOT NULL,
    [Complete] bit NOT NULL,
    [UnvaluedCount] int NOT NULL,
    [ValuationRunId] int NOT NULL,
    [ValuedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_StoredValuations] PRIMARY KEY ([Id]),
    -- R7: at most one stored valuation of an account and day.
    CONSTRAINT [UQ_StoredValuations_AccountId_ValuationDate] UNIQUE ([AccountId], [ValuationDate]),
    CONSTRAINT [UQ_StoredValuations_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_StoredValuations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_StoredValuations_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_StoredValuations_ValuationRuns_TenantId_ValuationRunId]
        FOREIGN KEY ([TenantId], [ValuationRunId]) REFERENCES [ValuationRuns] ([TenantId], [Id]),
    CONSTRAINT [FK_StoredValuations_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    -- account-valuation R16: the total is the cash plus the value of every Valued line.
    CONSTRAINT [CK_StoredValuations_Total] CHECK ([Total] = [Cash] + [HoldingsValue]),
    CONSTRAINT [CK_StoredValuations_Complete]
        CHECK (([Complete] = 1 AND [UnvaluedCount] = 0) OR ([Complete] = 0 AND [UnvaluedCount] > 0))
);

-- R26: funds under management of a past day is a sum of the rows of that day.
CREATE INDEX [IX_StoredValuations_TenantId_ValuationDate]
    ON [StoredValuations] ([TenantId], [ValuationDate]) INCLUDE ([AccountId], [Currency], [Total], [Complete]);

-- R8: one line per line of the valuation, with the price row and the rate row it read.
CREATE TABLE [StoredValuationLines] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [StoredValuationId] bigint NOT NULL,
    [InstrumentId] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [LedgerQuantity] decimal(18, 8) NOT NULL,
    [Status] int NOT NULL,
    [PriceAmount] decimal(18, 8) NULL,
    [PriceCurrency] char(3) NULL,
    [PriceDate] date NULL,
    [PriceSource] nvarchar(50) NULL,
    [MarketValue] decimal(18, 4) NULL,
    [FxRate] decimal(18, 8) NULL,
    [FxDate] date NULL,
    [FxSource] nvarchar(50) NULL,
    [Value] decimal(18, 4) NULL,
    CONSTRAINT [PK_StoredValuationLines] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_StoredValuationLines_StoredValuationId_InstrumentId] UNIQUE ([StoredValuationId], [InstrumentId]),
    CONSTRAINT [FK_StoredValuationLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_StoredValuationLines_StoredValuations_TenantId_StoredValuationId]
        FOREIGN KEY ([TenantId], [StoredValuationId]) REFERENCES [StoredValuations] ([TenantId], [Id]),
    CONSTRAINT [FK_StoredValuationLines_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_StoredValuationLines_Currencies_PriceCurrency] FOREIGN KEY ([PriceCurrency]) REFERENCES [Currencies] ([Code]),
    -- ValuationLineStatus: Valued 0 to NegativeQuantity 6.
    CONSTRAINT [CK_StoredValuationLines_Status] CHECK ([Status] BETWEEN 0 AND 6),
    CONSTRAINT [CK_StoredValuationLines_Price] CHECK (
        ([PriceAmount] IS NULL AND [PriceCurrency] IS NULL AND [PriceDate] IS NULL AND [PriceSource] IS NULL)
        OR ([PriceAmount] IS NOT NULL AND [PriceCurrency] IS NOT NULL AND [PriceDate] IS NOT NULL AND [PriceSource] IS NOT NULL)),
    CONSTRAINT [CK_StoredValuationLines_Fx] CHECK (
        ([FxRate] IS NULL AND [FxDate] IS NULL AND [FxSource] IS NULL)
        OR ([FxRate] IS NOT NULL AND [FxDate] IS NOT NULL AND [FxSource] IS NOT NULL)),
    -- account-valuation R14: a line has a value if and only if it is Valued.
    CONSTRAINT [CK_StoredValuationLines_Value] CHECK (([Status] = 0 AND [Value] IS NOT NULL) OR ([Status] <> 0 AND [Value] IS NULL))
);
