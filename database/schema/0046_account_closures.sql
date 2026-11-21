-- Account closure (docs/features/account-closure.md §6).
-- The account closures of Custodial accounts: who closed the account, when and why, the cash balance at request,
-- the payout (as a withdrawal to a nominated bank account) and the remainder written off. An Account has at most one
-- closure in progress. Widens the Transaction source checks for AccountClosure (5), which always has a source id
-- (the closure's PublicId), and the statement line unmatched reason for AccountClosing (12). Forward only; no
-- existing row changes and nothing is back-filled.

CREATE TABLE [AccountClosures] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [Status] int NOT NULL,
    [CancelReason] int NULL,
    [Memo] nvarchar(200) NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [RequestedAt] datetimeoffset NOT NULL,
    [Currency] char(3) NOT NULL,
    [CashBalanceAtRequest] decimal(18, 4) NOT NULL,
    [PayoutAmount] decimal(18, 4) NOT NULL,
    [NominatedBankAccountId] int NULL,
    [WithdrawalId] int NULL,
    [WriteOffAmount] decimal(18, 4) NULL,
    [WriteOffTransactionId] int NULL,
    [CompletedAt] datetimeoffset NULL,
    [EndedAt] datetimeoffset NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_AccountClosures] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_AccountClosures_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_AccountClosures_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_AccountClosures_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_AccountClosures_Users_TenantId_RequestedByUserId]
        FOREIGN KEY ([TenantId], [RequestedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_AccountClosures_NominatedBankAccounts_TenantId_NominatedBankAccountId]
        FOREIGN KEY ([TenantId], [NominatedBankAccountId]) REFERENCES [NominatedBankAccounts] ([TenantId], [Id]),
    CONSTRAINT [FK_AccountClosures_Withdrawals_TenantId_WithdrawalId]
        FOREIGN KEY ([TenantId], [WithdrawalId]) REFERENCES [Withdrawals] ([TenantId], [Id]),
    CONSTRAINT [FK_AccountClosures_Transactions_TenantId_WriteOffTransactionId]
        FOREIGN KEY ([TenantId], [WriteOffTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_AccountClosures_Status] CHECK ([Status] BETWEEN 0 AND 2),
    CONSTRAINT [CK_AccountClosures_CancelReason] CHECK ([CancelReason] BETWEEN 0 AND 3),
    CONSTRAINT [CK_AccountClosures_CashBalanceAtRequest] CHECK ([CashBalanceAtRequest] >= 0),
    CONSTRAINT [CK_AccountClosures_PayoutAmount] CHECK ([PayoutAmount] >= 0),
    CONSTRAINT [CK_AccountClosures_WriteOffAmount] CHECK ([WriteOffAmount] > 0),
    -- The cancel reason is set iff Cancelled.
    CONSTRAINT [CK_AccountClosures_Cancel] CHECK (
        ([Status] = 2 AND [CancelReason] IS NOT NULL) OR ([Status] <> 2 AND [CancelReason] IS NULL)),
    -- The completion time is set iff Completed; the end time iff Cancelled.
    CONSTRAINT [CK_AccountClosures_Completed] CHECK (
        ([Status] = 1 AND [CompletedAt] IS NOT NULL) OR ([Status] <> 1 AND [CompletedAt] IS NULL)),
    CONSTRAINT [CK_AccountClosures_Ended] CHECK (
        ([Status] = 2 AND [EndedAt] IS NOT NULL) OR ([Status] <> 2 AND [EndedAt] IS NULL)),
    -- The nominated bank account and the withdrawal are both set iff there is a payout.
    CONSTRAINT [CK_AccountClosures_Payout] CHECK (
        ([PayoutAmount] > 0 AND [NominatedBankAccountId] IS NOT NULL AND [WithdrawalId] IS NOT NULL)
        OR ([PayoutAmount] = 0 AND [NominatedBankAccountId] IS NULL AND [WithdrawalId] IS NULL)),
    -- The write-off amount and its Transaction are set together.
    CONSTRAINT [CK_AccountClosures_WriteOff] CHECK (
        ([WriteOffAmount] IS NULL AND [WriteOffTransactionId] IS NULL)
        OR ([WriteOffAmount] IS NOT NULL AND [WriteOffTransactionId] IS NOT NULL))
);

-- An Account has at most one closure in progress (§5).
CREATE UNIQUE INDEX [UX_AccountClosures_InProgress]
    ON [AccountClosures] ([TenantId], [AccountId]) WHERE [Status] = 0;

-- A withdrawal pays out at most one closure.
CREATE UNIQUE INDEX [UX_AccountClosures_Withdrawal]
    ON [AccountClosures] ([WithdrawalId]) WHERE [WithdrawalId] IS NOT NULL;

CREATE INDEX [IX_AccountClosures_TenantId_Status_RequestedAt]
    ON [AccountClosures] ([TenantId], [Status], [RequestedAt] DESC);

ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_UnmatchedReason];
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_UnmatchedReason] CHECK ([UnmatchedReason] BETWEEN 0 AND 12);

ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 5);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5) AND [SourcePublicId] IS NOT NULL));
