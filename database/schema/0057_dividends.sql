-- Dividends and dividend reinvestment (docs/features/dividends.md §6.1).
-- Corporate action kind Dividend (3) with its terms per share and no ratio; the receipt, payment and reinvestment
-- election of a dividend; the dividend fields of an entitlement; the dividend detail of a Dividend Transaction; cash
-- leg role Reinvestment (3) and tax kind ForeignWithholding (2); the standing reinvestment election of an Account;
-- a statement line matched to a dividend (method 7) and reason DividendMismatch (14); bank reconciliation item kinds
-- DividendsNotPosted (20) and DividendDifference (21). Forward only. No back-fill: no dividend exists before this
-- script, every existing corporate action keeps its ratio, and every existing account has ReinvestDividends = 0.
-- The script is one batch, so every statement that names a column added here runs through EXEC.

-- Corporate actions: the ratio becomes optional, the terms, the receipt, the payment and the election.
ALTER TABLE [CorporateActions] DROP CONSTRAINT [CK_CorporateActions_Kind];
ALTER TABLE [CorporateActions] DROP CONSTRAINT [CK_CorporateActions_Ratio];
ALTER TABLE [CorporateActions] ALTER COLUMN [RatioNew] int NULL;
ALTER TABLE [CorporateActions] ALTER COLUMN [RatioHeld] int NULL;
ALTER TABLE [CorporateActions] ADD
    [AmountPerShare] decimal(18, 8) NULL,
    [TaxSource] int NULL,
    [ImputationCreditPerShare] decimal(18, 8) NULL,
    [ForeignTaxRatePercent] decimal(5, 2) NULL,
    [ReinvestmentPrice] decimal(18, 8) NULL,
    [ReceiptLineId] int NULL,
    [ReceivedAmount] decimal(18, 4) NULL,
    [PaidAt] datetimeoffset NULL,
    [ElectionStatus] int NULL,
    [ElectionQuantity] decimal(18, 8) NULL,
    [SafekeepingAccountId] int NULL,
    [ElectionAttempts] int NOT NULL CONSTRAINT [DF_CorporateActions_ElectionAttempts] DEFAULT 0,
    [ElectionLastAttemptAt] datetimeoffset NULL,
    [ElectionReason] int NULL,
    [SharesMovementId] int NULL,
    [SharesReceived] decimal(18, 8) NULL,
    [ReleasedByUserId] int NULL,
    [ReleasedAt] datetimeoffset NULL,
    [ReleaseNote] nvarchar(200) NULL;

EXEC(N'
-- CorporateActionKind: Split, Consolidation, Bonus, Dividend (R2).
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_Kind] CHECK ([Kind] BETWEEN 0 AND 3);
-- A dividend has terms and no ratio; any other kind a ratio and none of the dividend columns (R2, R3).
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_Terms] CHECK (
    ([Kind] = 3 AND [RatioNew] IS NULL AND [RatioHeld] IS NULL
        AND [AmountPerShare] IS NOT NULL AND [AmountPerShare] > 0
        AND [TaxSource] IS NOT NULL AND [TaxSource] BETWEEN 0 AND 1
        AND [ImputationCreditPerShare] IS NOT NULL AND [ImputationCreditPerShare] >= 0
        AND [ImputationCreditPerShare] <= [AmountPerShare]
        AND [ForeignTaxRatePercent] IS NOT NULL AND [ForeignTaxRatePercent] >= 0 AND [ForeignTaxRatePercent] < 100
        AND ([ReinvestmentPrice] IS NULL OR [ReinvestmentPrice] > 0))
    OR ([Kind] <> 3 AND [RatioNew] IS NOT NULL AND [RatioHeld] IS NOT NULL
        AND [RatioNew] BETWEEN 1 AND 10000 AND [RatioHeld] BETWEEN 1 AND 10000
        AND [AmountPerShare] IS NULL AND [TaxSource] IS NULL AND [ImputationCreditPerShare] IS NULL
        AND [ForeignTaxRatePercent] IS NULL AND [ReinvestmentPrice] IS NULL));
-- The receipt, the payment and the election belong to a dividend only (R20–R31).
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_DividendOnly] CHECK (
    [Kind] = 3 OR ([ReceiptLineId] IS NULL AND [ReceivedAmount] IS NULL AND [PaidAt] IS NULL
        AND [ElectionStatus] IS NULL AND [ElectionQuantity] IS NULL AND [SafekeepingAccountId] IS NULL
        AND [ElectionAttempts] = 0 AND [ElectionLastAttemptAt] IS NULL AND [ElectionReason] IS NULL
        AND [SharesMovementId] IS NULL AND [SharesReceived] IS NULL AND [ReleasedByUserId] IS NULL
        AND [ReleasedAt] IS NULL AND [ReleaseNote] IS NULL));
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_Receipt] CHECK (
    ([ReceiptLineId] IS NULL AND [ReceivedAmount] IS NULL) OR ([ReceiptLineId] IS NOT NULL AND [ReceivedAmount] IS NOT NULL));
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_PaidAt] CHECK ([PaidAt] IS NULL OR [ReceiptLineId] IS NOT NULL);
-- ReinvestmentElectionStatus: None, Pending, Accepted, Rejected, Lapsed.
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_ElectionStatus] CHECK ([ElectionStatus] BETWEEN 0 AND 4);
-- ElectionRejectReason: UnknownCorporateAction, NoPlan, NotEntitled, TooLate, Other; only on a Rejected election.
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_ElectionReason] CHECK (
    ([ElectionReason] IS NULL) OR ([ElectionReason] BETWEEN 0 AND 4 AND [ElectionStatus] = 3));
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_ElectionAttempts] CHECK ([ElectionAttempts] >= 0);
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_ElectionQuantity] CHECK (
    [ElectionQuantity] IS NULL OR [ElectionQuantity] > 0);
ALTER TABLE [CorporateActions] ADD CONSTRAINT [CK_CorporateActions_SharesReceived] CHECK (
    ([SharesReceived] IS NULL AND [SharesMovementId] IS NULL)
    OR ([SharesReceived] IS NOT NULL AND [SharesReceived] > 0 AND [SharesMovementId] IS NOT NULL));
ALTER TABLE [CorporateActions] ADD CONSTRAINT [FK_CorporateActions_BankStatementLines_TenantId_ReceiptLineId]
    FOREIGN KEY ([TenantId], [ReceiptLineId]) REFERENCES [BankStatementLines] ([TenantId], [Id]);
ALTER TABLE [CorporateActions] ADD CONSTRAINT [FK_CorporateActions_SafekeepingAccounts_TenantId_SafekeepingAccountId]
    FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]);
ALTER TABLE [CorporateActions] ADD CONSTRAINT [FK_CorporateActions_CustodyMovements_TenantId_SharesMovementId]
    FOREIGN KEY ([TenantId], [SharesMovementId]) REFERENCES [CustodyMovements] ([TenantId], [Id]);
ALTER TABLE [CorporateActions] ADD CONSTRAINT [FK_CorporateActions_Users_TenantId_ReleasedByUserId]
    FOREIGN KEY ([TenantId], [ReleasedByUserId]) REFERENCES [Users] ([TenantId], [Id]);
-- A statement line is the receipt of at most one dividend.
CREATE UNIQUE INDEX [UX_CorporateActions_ReceiptLineId] ON [CorporateActions] ([ReceiptLineId])
    WHERE [ReceiptLineId] IS NOT NULL;
-- R22: the elections the custodian job sends.
CREATE INDEX [IX_CorporateActions_ElectionStatus] ON [CorporateActions] ([ElectionStatus])
    WHERE [ElectionStatus] = 1;
');

-- Corporate action announcements: the ratio becomes optional, the terms.
ALTER TABLE [CorporateActionAnnouncements] DROP CONSTRAINT [CK_CorporateActionAnnouncements_Kind];
ALTER TABLE [CorporateActionAnnouncements] DROP CONSTRAINT [CK_CorporateActionAnnouncements_Ratio];
ALTER TABLE [CorporateActionAnnouncements] ALTER COLUMN [RatioNew] int NULL;
ALTER TABLE [CorporateActionAnnouncements] ALTER COLUMN [RatioHeld] int NULL;
ALTER TABLE [CorporateActionAnnouncements] ADD
    [AmountPerShare] decimal(18, 8) NULL,
    [TaxSource] int NULL,
    [ImputationCreditPerShare] decimal(18, 8) NULL,
    [ForeignTaxRatePercent] decimal(5, 2) NULL,
    [ReinvestmentPrice] decimal(18, 8) NULL;

EXEC(N'
ALTER TABLE [CorporateActionAnnouncements] ADD CONSTRAINT [CK_CorporateActionAnnouncements_Kind] CHECK ([Kind] BETWEEN 0 AND 3);
-- R8: a dividend has an amount and a tax source and no ratio; a ratio kind has both ratio numbers; an unknown kind
-- has no rule.
ALTER TABLE [CorporateActionAnnouncements] ADD CONSTRAINT [CK_CorporateActionAnnouncements_Terms] CHECK (
    [Kind] IS NULL
    OR ([Kind] = 3 AND [AmountPerShare] IS NOT NULL AND [TaxSource] IS NOT NULL
        AND [RatioNew] IS NULL AND [RatioHeld] IS NULL)
    OR ([Kind] BETWEEN 0 AND 2 AND [RatioNew] IS NOT NULL AND [RatioHeld] IS NOT NULL
        AND [RatioNew] >= 1 AND [RatioHeld] >= 1));
');

-- Entitlements: the dividend fields and the reason NothingToPay.
ALTER TABLE [CorporateActionEntitlements] DROP CONSTRAINT [CK_CorporateActionEntitlements_Reason];
ALTER TABLE [CorporateActionEntitlements] DROP CONSTRAINT [CK_CorporateActionEntitlements_Posted];
ALTER TABLE [CorporateActionEntitlements] ADD
    [EntitledQuantity] decimal(18, 8) NULL,
    [GrossAmount] decimal(18, 4) NULL,
    [ImputationCredit] decimal(18, 4) NULL,
    [ForeignTax] decimal(18, 4) NULL,
    [Reinvests] bit NOT NULL CONSTRAINT [DF_CorporateActionEntitlements_Reinvests] DEFAULT 0,
    [ParticipatingQuantity] decimal(18, 8) NULL,
    [ReinvestedQuantity] decimal(18, 8) NULL,
    [NetAmount] decimal(18, 4) NULL,
    [NetCurrency] char(3) NULL;

EXEC(N'
-- EntitlementReason: NoHolding, TooSmall, NothingToPay (R14).
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [CK_CorporateActionEntitlements_Reason] CHECK ([Reason] BETWEEN 0 AND 2);
-- Posted iff it has the Transaction, and either both quantities (a ratio kind) or the net of a dividend.
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [CK_CorporateActionEntitlements_Posted] CHECK (
    ([Status] = 1 AND [TransactionId] IS NOT NULL
        AND (([QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL AND [NetAmount] IS NULL AND [NetCurrency] IS NULL)
            OR ([QuantityBefore] IS NULL AND [QuantityAfter] IS NULL AND [EntitledQuantity] IS NOT NULL
                AND [NetAmount] IS NOT NULL AND [NetCurrency] IS NOT NULL)))
    OR ([Status] <> 1 AND [TransactionId] IS NULL AND [QuantityBefore] IS NULL AND [QuantityAfter] IS NULL
        AND [NetAmount] IS NULL AND [NetCurrency] IS NULL));
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [CK_CorporateActionEntitlements_EntitledQuantity] CHECK (
    [EntitledQuantity] IS NULL OR [EntitledQuantity] > 0);
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [CK_CorporateActionEntitlements_SourceAmounts] CHECK (
    ([EntitledQuantity] IS NULL AND [GrossAmount] IS NULL AND [ImputationCredit] IS NULL AND [ForeignTax] IS NULL)
    OR ([EntitledQuantity] IS NOT NULL AND [GrossAmount] IS NOT NULL AND [ImputationCredit] IS NOT NULL
        AND [ForeignTax] IS NOT NULL AND [GrossAmount] >= 0 AND [ImputationCredit] >= 0 AND [ForeignTax] >= 0));
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [CK_CorporateActionEntitlements_Reinvests] CHECK (
    [Reinvests] = 0 OR ([ParticipatingQuantity] IS NOT NULL AND [ParticipatingQuantity] > 0));
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [CK_CorporateActionEntitlements_ReinvestedQuantity] CHECK (
    [ReinvestedQuantity] IS NULL OR ([Reinvests] = 1 AND [ReinvestedQuantity] > 0));
ALTER TABLE [CorporateActionEntitlements] ADD CONSTRAINT [FK_CorporateActionEntitlements_Currencies_NetCurrency]
    FOREIGN KEY ([NetCurrency]) REFERENCES [Currencies] ([Code]);
');

-- R39: the dividend detail of a Dividend Transaction and of its Reversal.
CREATE TABLE [TransactionDividendDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [AmountPerShare] decimal(18, 8) NOT NULL,
    [Currency] char(3) NOT NULL,
    [TaxSource] int NOT NULL,
    [ImputationCredit] decimal(18, 4) NOT NULL,
    [ReinvestedQuantity] decimal(18, 8) NULL,
    [ReinvestmentPrice] decimal(18, 8) NULL,
    [ExDate] date NOT NULL,
    [RecordDate] date NULL,
    [PaymentDate] date NULL,
    CONSTRAINT [PK_TransactionDividendDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionDividendDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionDividendDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionDividendDetails_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_TransactionDividendDetails_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_TransactionDividendDetails_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_TransactionDividendDetails_AmountPerShare] CHECK ([AmountPerShare] > 0),
    CONSTRAINT [CK_TransactionDividendDetails_TaxSource] CHECK ([TaxSource] BETWEEN 0 AND 1),
    CONSTRAINT [CK_TransactionDividendDetails_ImputationCredit] CHECK ([ImputationCredit] >= 0),
    CONSTRAINT [CK_TransactionDividendDetails_Reinvestment] CHECK (
        ([ReinvestedQuantity] IS NULL AND [ReinvestmentPrice] IS NULL)
        OR ([ReinvestedQuantity] IS NOT NULL AND [ReinvestmentPrice] IS NOT NULL
            AND [ReinvestedQuantity] > 0 AND [ReinvestmentPrice] > 0))
);

CREATE INDEX [IX_TransactionDividendDetails_TenantId] ON [TransactionDividendDetails] ([TenantId]);

-- R40: cash leg role Reinvestment = 3. R38: tax kind ForeignWithholding = 2.
ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [CK_TransactionCashLegs_Role];
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [CK_TransactionCashLegs_Role] CHECK ([Role] BETWEEN 0 AND 3);
ALTER TABLE [TransactionTaxDetails] DROP CONSTRAINT [CK_TransactionTaxDetails_Kind];
ALTER TABLE [TransactionTaxDetails] ADD CONSTRAINT [CK_TransactionTaxDetails_Kind] CHECK ([Kind] BETWEEN 0 AND 2);

-- R17: the standing reinvestment election of a Custodial account.
ALTER TABLE [Accounts] ADD
    [ReinvestDividends] bit NOT NULL CONSTRAINT [DF_Accounts_ReinvestDividends] DEFAULT 0,
    [ReinvestDividendsChangedByUserId] int NULL,
    [ReinvestDividendsChangedAt] datetimeoffset NULL;

EXEC(N'
ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Accounts_ReinvestDividends] CHECK ([ReinvestDividends] = 0 OR [CustodyMode] = 1);
ALTER TABLE [Accounts] ADD CONSTRAINT [FK_Accounts_Users_TenantId_ReinvestDividendsChangedByUserId]
    FOREIGN KEY ([TenantId], [ReinvestDividendsChangedByUserId]) REFERENCES [Users] ([TenantId], [Id]);
');

-- R24, R26: a statement line matched to a dividend; method Dividend (7), reason DividendMismatch (14).
ALTER TABLE [BankStatementLines] ADD [CorporateActionId] int NULL;
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_MatchMethod];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_UnmatchedReason];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_Match];
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_MatchMethod] CHECK ([MatchMethod] BETWEEN 0 AND 7);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_UnmatchedReason] CHECK ([UnmatchedReason] BETWEEN 0 AND 14);
EXEC(N'
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [FK_BankStatementLines_CorporateActions_TenantId_CorporateActionId]
    FOREIGN KEY ([TenantId], [CorporateActionId]) REFERENCES [CorporateActions] ([TenantId], [Id]);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_Match] CHECK (
    ([Status] = 1 AND [MatchMethod] IS NOT NULL AND [MatchedAt] IS NOT NULL
        AND (([MatchedAccountId] IS NOT NULL AND [MatchedTransactionId] IS NOT NULL AND [TradeSettlementId] IS NULL
                AND [MigrationBatchId] IS NULL AND [CorporateActionId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [RelatedLineId] IS NOT NULL
                AND [TradeSettlementId] IS NULL AND [MigrationBatchId] IS NULL AND [CorporateActionId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [TradeSettlementId] IS NOT NULL
                AND [MigrationBatchId] IS NULL AND [CorporateActionId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [TradeSettlementId] IS NULL
                AND [MigrationBatchId] IS NOT NULL AND [CorporateActionId] IS NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [TradeSettlementId] IS NULL
                AND [MigrationBatchId] IS NULL AND [CorporateActionId] IS NOT NULL)))
    OR ([Status] <> 1 AND [MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL
        AND [MatchMethod] IS NULL AND [MatchedAt] IS NULL AND [TradeSettlementId] IS NULL AND [MigrationBatchId] IS NULL
        AND [CorporateActionId] IS NULL));
CREATE INDEX [IX_BankStatementLines_CorporateActionId] ON [BankStatementLines] ([CorporateActionId])
    WHERE [CorporateActionId] IS NOT NULL;
');

-- R44: DividendsNotPosted (20) and DividendDifference (21); neither carries a line.
ALTER TABLE [BankReconciliationItems] DROP CONSTRAINT [CK_BankReconciliationItems_Kind];
ALTER TABLE [BankReconciliationItems] ADD CONSTRAINT [CK_BankReconciliationItems_Kind] CHECK ([Kind] BETWEEN 0 AND 21);
