-- Dividends at the simulator market and custodian (docs/features/dividends.md §6.2, R54–R64).
-- A market corporate action may be a dividend: terms per share in place of a ratio, a payment date, and the day the
-- custodian paid it. A custodian account may name the bank account its income is paid into. One dividend payment per
-- dividend and custodian account with a position when it went ex, with the election the platform sent for it; the
-- first answer of a rejected election; and the income mode. A reset empties the payments and the rejected elections
-- and sets the income mode to Pay (R63). The script is one batch, so statements that name a new column run in EXEC.

ALTER TABLE [MarketCorporateActions] DROP CONSTRAINT [CK_MarketCorporateActions_Kind];
ALTER TABLE [MarketCorporateActions] DROP CONSTRAINT [CK_MarketCorporateActions_Ratio];
ALTER TABLE [MarketCorporateActions] ALTER COLUMN [RatioNew] int NULL;
ALTER TABLE [MarketCorporateActions] ALTER COLUMN [RatioHeld] int NULL;
ALTER TABLE [MarketCorporateActions] ADD
    [AmountPerShare] decimal(18, 8) NULL,
    [TaxSource] int NULL,
    [ImputationCreditPerShare] decimal(18, 8) NULL,
    [ForeignTaxRatePercent] decimal(5, 2) NULL,
    [ReinvestmentPrice] decimal(18, 8) NULL,
    [PaidOn] date NULL;

ALTER TABLE [MarketCorporateActionVersions] ALTER COLUMN [RatioNew] int NULL;
ALTER TABLE [MarketCorporateActionVersions] ALTER COLUMN [RatioHeld] int NULL;
ALTER TABLE [MarketCorporateActionVersions] ADD
    [AmountPerShare] decimal(18, 8) NULL,
    [TaxSource] int NULL,
    [ImputationCreditPerShare] decimal(18, 8) NULL,
    [ForeignTaxRatePercent] decimal(5, 2) NULL,
    [ReinvestmentPrice] decimal(18, 8) NULL;

EXEC(N'
-- Split, Consolidation, Bonus, Dividend.
ALTER TABLE [MarketCorporateActions] ADD CONSTRAINT [CK_MarketCorporateActions_Kind] CHECK ([Kind] BETWEEN 0 AND 3);
-- A dividend has terms and no ratio; any other kind a ratio and no terms.
ALTER TABLE [MarketCorporateActions] ADD CONSTRAINT [CK_MarketCorporateActions_Terms] CHECK (
    ([Kind] = 3 AND [RatioNew] IS NULL AND [RatioHeld] IS NULL AND [AmountPerShare] IS NOT NULL AND [AmountPerShare] > 0
        AND [TaxSource] IS NOT NULL AND [TaxSource] BETWEEN 0 AND 1)
    OR ([Kind] <> 3 AND [RatioNew] IS NOT NULL AND [RatioHeld] IS NOT NULL
        AND [RatioNew] BETWEEN 1 AND 10000 AND [RatioHeld] BETWEEN 1 AND 10000
        AND [AmountPerShare] IS NULL AND [TaxSource] IS NULL AND [PaidOn] IS NULL));
');

ALTER TABLE [CustodianAccounts] ADD [IncomeAccountNumber] nvarchar(34) NULL;

ALTER TABLE [CustodianSettings] ADD [IncomeMode] int NOT NULL CONSTRAINT [DF_CustodianSettings_IncomeMode] DEFAULT 0;
EXEC(N'
-- Pay, Hold.
ALTER TABLE [CustodianSettings] ADD CONSTRAINT [CK_CustodianSettings_IncomeMode] CHECK ([IncomeMode] BETWEEN 0 AND 1);
');

CREATE TABLE [CustodianDividendPayments] (
    [Id] int NOT NULL IDENTITY,
    [ActionId] int NOT NULL,
    [AccountId] int NOT NULL,
    [EntitledPosition] decimal(18, 8) NOT NULL,
    [ElectionInstructionId] nvarchar(100) NULL,
    [ElectionBody] nvarchar(max) NULL,
    [ElectedQuantity] decimal(18, 8) NULL,
    [Shares] decimal(18, 8) NULL,
    [Cash] decimal(18, 2) NULL,
    -- Due, Held, Paid.
    [Status] int NOT NULL,
    [BankLineId] int NULL,
    [MovementId] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CustodianDividendPayments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustodianDividendPayments_MarketCorporateActions_ActionId]
        FOREIGN KEY ([ActionId]) REFERENCES [MarketCorporateActions] ([Id]),
    CONSTRAINT [FK_CustodianDividendPayments_CustodianAccounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [CustodianAccounts] ([Id]),
    CONSTRAINT [CK_CustodianDividendPayments_Status] CHECK ([Status] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CustodianDividendPayments_EntitledPosition] CHECK ([EntitledPosition] > 0),
    CONSTRAINT [UX_CustodianDividendPayments_ActionId_AccountId] UNIQUE ([ActionId], [AccountId])
);

CREATE UNIQUE INDEX [UX_CustodianDividendPayments_AccountId_ElectionInstructionId]
    ON [CustodianDividendPayments] ([AccountId], [ElectionInstructionId]) WHERE [ElectionInstructionId] IS NOT NULL;

-- R58: the first answer of a rejected election, so the same instruction gets the same answer.
CREATE TABLE [CustodianRejectedElections] (
    [Id] int NOT NULL IDENTITY,
    [AccountId] int NOT NULL,
    [InstructionId] nvarchar(100) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [Reason] nvarchar(50) NOT NULL,
    CONSTRAINT [PK_CustodianRejectedElections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustodianRejectedElections_CustodianAccounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [CustodianAccounts] ([Id]),
    CONSTRAINT [UX_CustodianRejectedElections_AccountId_InstructionId] UNIQUE ([AccountId], [InstructionId])
);
