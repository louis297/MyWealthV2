-- Interest (docs/features/interest-run.md §6.2).
-- Tenant interest rate schedules with marginal bands of annual rates; one daily accrual row per Custodial account
-- and UTC day, written once and stamped with the Transaction that pays it; stored interest runs; a Tax cash leg
-- role with its tax detail and an interest detail per posting; source kind InterestRun (6); the kind of a bank
-- statement line; three reconciliation item kinds. Forward only. No back-fill: no account was accrued before this
-- script, every existing cash leg keeps role 0 or 1, every stored statement line is a transfer, and no Transaction
-- has source InterestRun.

CREATE TABLE [InterestRateSchedules] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [EffectiveFrom] date NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InterestRateSchedules] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_InterestRateSchedules_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_InterestRateSchedules_TenantId_Currency_EffectiveFrom] UNIQUE ([TenantId], [Currency], [EffectiveFrom]),
    CONSTRAINT [UQ_InterestRateSchedules_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_InterestRateSchedules_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_InterestRateSchedules_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code])
);

CREATE TABLE [InterestRateBands] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [InterestRateScheduleId] int NOT NULL,
    [FromAmount] decimal(18, 4) NOT NULL,
    [RatePercent] decimal(7, 4) NOT NULL,
    CONSTRAINT [PK_InterestRateBands] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_InterestRateBands_InterestRateScheduleId_FromAmount] UNIQUE ([InterestRateScheduleId], [FromAmount]),
    CONSTRAINT [FK_InterestRateBands_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_InterestRateBands_InterestRateSchedules_TenantId_InterestRateScheduleId]
        FOREIGN KEY ([TenantId], [InterestRateScheduleId]) REFERENCES [InterestRateSchedules] ([TenantId], [Id]),
    CONSTRAINT [CK_InterestRateBands_FromAmount] CHECK ([FromAmount] >= 0),
    CONSTRAINT [CK_InterestRateBands_RatePercent] CHECK ([RatePercent] >= 0 AND [RatePercent] <= 100)
);

-- R19: the last UTC day the account was accrued through; null before the first time.
ALTER TABLE [Accounts] ADD [InterestAccruedThrough] date NULL;

CREATE TABLE [InterestAccruals] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [AccrualDate] date NOT NULL,
    [Balance] decimal(18, 4) NOT NULL,
    [InterestRateScheduleId] int NOT NULL,
    [EffectiveRatePercent] decimal(7, 4) NOT NULL,
    [Amount] decimal(18, 8) NOT NULL,
    [PostedTransactionId] int NULL,
    CONSTRAINT [PK_InterestAccruals] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_InterestAccruals_AccountId_AccrualDate] UNIQUE ([AccountId], [AccrualDate]),
    CONSTRAINT [FK_InterestAccruals_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_InterestAccruals_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_InterestAccruals_InterestRateSchedules_TenantId_InterestRateScheduleId]
        FOREIGN KEY ([TenantId], [InterestRateScheduleId]) REFERENCES [InterestRateSchedules] ([TenantId], [Id]),
    CONSTRAINT [FK_InterestAccruals_Transactions_TenantId_PostedTransactionId]
        FOREIGN KEY ([TenantId], [PostedTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_InterestAccruals_Balance] CHECK ([Balance] > 0),
    CONSTRAINT [CK_InterestAccruals_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_InterestAccruals_EffectiveRatePercent] CHECK ([EffectiveRatePercent] >= 0 AND [EffectiveRatePercent] <= 100)
);

-- R21: the unposted rows of an account, by day.
CREATE INDEX [IX_InterestAccruals_AccountId_AccrualDate_Unposted]
    ON [InterestAccruals] ([AccountId], [AccrualDate]) WHERE [PostedTransactionId] IS NULL;

CREATE INDEX [IX_InterestAccruals_PostedTransactionId] ON [InterestAccruals] ([PostedTransactionId]);

CREATE TABLE [InterestRuns] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AsOfDate] date NOT NULL,
    [Trigger] int NOT NULL,
    [RanByUserId] int NULL,
    [AccountId] int NULL,
    [StartedAt] datetimeoffset NOT NULL,
    [CompletedAt] datetimeoffset NULL,
    [AccountsProcessed] int NOT NULL,
    [AccountsFailed] int NOT NULL,
    [PostingCount] int NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_InterestRuns] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_InterestRuns_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_InterestRuns_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    -- A SystemAdmin has no tenant, so the person is not a tenant composite key.
    CONSTRAINT [FK_InterestRuns_Users_RanByUserId] FOREIGN KEY ([RanByUserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_InterestRuns_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [CK_InterestRuns_Trigger] CHECK ([Trigger] BETWEEN 0 AND 2),
    -- R30: no person for the daily job, a person for a manual run and an account closure.
    CONSTRAINT [CK_InterestRuns_RanBy] CHECK (([Trigger] = 0 AND [RanByUserId] IS NULL) OR ([Trigger] <> 0 AND [RanByUserId] IS NOT NULL)),
    -- R33: the account only for an account closure.
    CONSTRAINT [CK_InterestRuns_Account] CHECK (([Trigger] = 2 AND [AccountId] IS NOT NULL) OR ([Trigger] <> 2 AND [AccountId] IS NULL)),
    CONSTRAINT [CK_InterestRuns_Counts] CHECK ([AccountsProcessed] >= 0 AND [AccountsFailed] >= 0 AND [PostingCount] >= 0
        AND [AccountsFailed] <= [AccountsProcessed])
);

CREATE INDEX [IX_InterestRuns_TenantId_StartedAt] ON [InterestRuns] ([TenantId], [StartedAt] DESC);

-- R25: cash leg role Tax = 2.
ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [CK_TransactionCashLegs_Role];
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [CK_TransactionCashLegs_Role] CHECK ([Role] BETWEEN 0 AND 2);

CREATE TABLE [TransactionTaxDetails] (
    [CashLegId] int NOT NULL,
    [TenantId] int NOT NULL,
    [TransactionId] int NOT NULL,
    [Kind] int NOT NULL,
    [RatePercent] decimal(5, 2) NOT NULL,
    [BasisAmount] decimal(18, 4) NOT NULL,
    [TaxAmount] decimal(18, 4) NOT NULL,
    [Currency] char(3) NOT NULL,
    CONSTRAINT [PK_TransactionTaxDetails] PRIMARY KEY ([CashLegId]),
    CONSTRAINT [UQ_TransactionTaxDetails_TransactionId_Kind] UNIQUE ([TransactionId], [Kind]),
    CONSTRAINT [FK_TransactionTaxDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionTaxDetails_TransactionCashLegs_TenantId_CashLegId]
        FOREIGN KEY ([TenantId], [CashLegId]) REFERENCES [TransactionCashLegs] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionTaxDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionTaxDetails_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_TransactionTaxDetails_Kind] CHECK ([Kind] BETWEEN 0 AND 1),
    CONSTRAINT [CK_TransactionTaxDetails_RatePercent] CHECK ([RatePercent] > 0 AND [RatePercent] <= 100),
    CONSTRAINT [CK_TransactionTaxDetails_BasisAmount] CHECK ([BasisAmount] > 0),
    CONSTRAINT [CK_TransactionTaxDetails_TaxAmount] CHECK ([TaxAmount] > 0)
);

CREATE TABLE [TransactionInterestDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [PeriodFrom] date NOT NULL,
    [PeriodTo] date NOT NULL,
    [Days] int NOT NULL,
    [AccruedAmount] decimal(18, 8) NOT NULL,
    [GrossAmount] decimal(18, 4) NOT NULL,
    CONSTRAINT [PK_TransactionInterestDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionInterestDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionInterestDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_TransactionInterestDetails_Period] CHECK ([PeriodFrom] <= [PeriodTo]),
    CONSTRAINT [CK_TransactionInterestDetails_Days] CHECK ([Days] >= 1),
    CONSTRAINT [CK_TransactionInterestDetails_AccruedAmount] CHECK ([AccruedAmount] > 0),
    CONSTRAINT [CK_TransactionInterestDetails_GrossAmount] CHECK ([GrossAmount] > 0)
);

CREATE INDEX [IX_TransactionInterestDetails_TenantId] ON [TransactionInterestDetails] ([TenantId]);
CREATE INDEX [IX_TransactionTaxDetails_TenantId] ON [TransactionTaxDetails] ([TenantId]);

-- Transaction source kind InterestRun = 6, with the run's PublicId as source id.
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 6);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5, 6) AND [SourcePublicId] IS NOT NULL));

-- R39: the kind of a bank statement line (0 Transfer, 1 Interest); stored lines are transfers.
ALTER TABLE [BankStatementLines] ADD [Kind] int NOT NULL CONSTRAINT [DF_BankStatementLines_Kind] DEFAULT (0);
EXEC(N'ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_Kind] CHECK ([Kind] BETWEEN 0 AND 1);');

-- R41: BankInterest (12) carries its line; InterestCredited (13) and TaxWithheld (14) do not.
ALTER TABLE [BankReconciliationItems] DROP CONSTRAINT [CK_BankReconciliationItems_Kind];
ALTER TABLE [BankReconciliationItems] DROP CONSTRAINT [CK_BankReconciliationItems_Line];
ALTER TABLE [BankReconciliationItems] ADD CONSTRAINT [CK_BankReconciliationItems_Kind] CHECK ([Kind] BETWEEN 0 AND 14);
ALTER TABLE [BankReconciliationItems] ADD CONSTRAINT [CK_BankReconciliationItems_Line] CHECK (
    ([Kind] IN (2, 3, 4, 6, 12) AND [StatementLineId] IS NOT NULL AND [Direction] IS NOT NULL
        AND [ValueDate] IS NOT NULL AND [LineStatus] IS NOT NULL)
    OR ([Kind] NOT IN (2, 3, 4, 6, 12) AND [StatementLineId] IS NULL AND [Direction] IS NULL
        AND [ValueDate] IS NULL AND [LineStatus] IS NULL AND [UnmatchedReason] IS NULL AND [Note] IS NULL));
