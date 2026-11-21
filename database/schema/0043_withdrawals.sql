-- Withdrawals (docs/features/withdrawals.md §6.2).
-- Withdrawals, the outgoing payments the worker sends for them, the payment detail of each payout and its
-- Reversal, and the statement line columns that find a payout line (end-to-end id) and a returned payout line
-- (the line it returns). Widens the Transaction source checks for Payment (4), which always has a source id (the
-- withdrawal's PublicId). Forward only; no existing row changes.

CREATE TABLE [Withdrawals] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [NominatedBankAccountId] int NOT NULL,
    [Amount] decimal(18, 4) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Reference] nvarchar(18) NOT NULL,
    [Memo] nvarchar(200) NULL,
    [Status] int NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [RequestedAt] datetimeoffset NOT NULL,
    [DecisionAction] int NULL,
    [DecisionByUserId] int NULL,
    [DecisionAt] datetimeoffset NULL,
    [DecisionNote] nvarchar(200) NULL,
    [PayoutTransactionId] int NULL,
    [ReturnTransactionId] int NULL,
    [FailureReason] int NULL,
    [ReturnReason] nvarchar(140) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Withdrawals] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Withdrawals_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Withdrawals_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_Withdrawals_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Withdrawals_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_Withdrawals_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_Withdrawals_NominatedBankAccounts_TenantId_NominatedBankAccountId]
        FOREIGN KEY ([TenantId], [NominatedBankAccountId]) REFERENCES [NominatedBankAccounts] ([TenantId], [Id]),
    CONSTRAINT [FK_Withdrawals_Users_TenantId_RequestedByUserId]
        FOREIGN KEY ([TenantId], [RequestedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_Withdrawals_Users_TenantId_DecisionByUserId]
        FOREIGN KEY ([TenantId], [DecisionByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_Withdrawals_Transactions_TenantId_PayoutTransactionId]
        FOREIGN KEY ([TenantId], [PayoutTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_Withdrawals_Transactions_TenantId_ReturnTransactionId]
        FOREIGN KEY ([TenantId], [ReturnTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_Withdrawals_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_Withdrawals_Status] CHECK ([Status] BETWEEN 0 AND 7),
    CONSTRAINT [CK_Withdrawals_DecisionAction] CHECK ([DecisionAction] BETWEEN 0 AND 2),
    CONSTRAINT [CK_Withdrawals_FailureReason] CHECK ([FailureReason] BETWEEN 0 AND 3),
    -- A decision is all null, or all set but the note.
    CONSTRAINT [CK_Withdrawals_Decision] CHECK (
        ([DecisionAction] IS NULL AND [DecisionByUserId] IS NULL AND [DecisionAt] IS NULL AND [DecisionNote] IS NULL)
        OR ([DecisionAction] IS NOT NULL AND [DecisionByUserId] IS NOT NULL AND [DecisionAt] IS NOT NULL)),
    -- The payout is set iff Paid or Returned; the return Reversal iff Returned; the failure iff Failed.
    CONSTRAINT [CK_Withdrawals_Payout] CHECK (
        ([Status] IN (3, 7) AND [PayoutTransactionId] IS NOT NULL)
        OR ([Status] NOT IN (3, 7) AND [PayoutTransactionId] IS NULL)),
    CONSTRAINT [CK_Withdrawals_Return] CHECK (
        ([Status] = 7 AND [ReturnTransactionId] IS NOT NULL) OR ([Status] <> 7 AND [ReturnTransactionId] IS NULL)),
    CONSTRAINT [CK_Withdrawals_Failure] CHECK (
        ([Status] = 6 AND [FailureReason] IS NOT NULL) OR ([Status] <> 6 AND [FailureReason] IS NULL))
);

-- Reserved cash sums the open withdrawals of an account (R35).
CREATE INDEX [IX_Withdrawals_TenantId_AccountId_Status] ON [Withdrawals] ([TenantId], [AccountId], [Status]);

-- The withdrawals list (R26).
CREATE INDEX [IX_Withdrawals_TenantId_Status_RequestedAt] ON [Withdrawals] ([TenantId], [Status], [RequestedAt] DESC);

CREATE TABLE [OutgoingPayments] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [ClientMoneyAccountId] int NOT NULL,
    [Connection] nvarchar(50) NOT NULL,
    [InstructionPublicId] uniqueidentifier NOT NULL,
    [WithdrawalId] int NOT NULL,
    [Amount] decimal(18, 4) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Reference] nvarchar(18) NULL,
    [Status] int NOT NULL,
    [Attempts] int NOT NULL CONSTRAINT [DF_OutgoingPayments_Attempts] DEFAULT 0,
    [LastAttemptAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [BankPaymentId] nvarchar(100) NULL,
    [AcceptedAt] datetimeoffset NULL,
    [RejectReason] int NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_OutgoingPayments] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_OutgoingPayments_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_OutgoingPayments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_OutgoingPayments_ClientMoneyAccounts_ClientMoneyAccountId]
        FOREIGN KEY ([ClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([Id]),
    CONSTRAINT [FK_OutgoingPayments_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_OutgoingPayments_Withdrawals_TenantId_WithdrawalId]
        FOREIGN KEY ([TenantId], [WithdrawalId]) REFERENCES [Withdrawals] ([TenantId], [Id]),
    CONSTRAINT [CK_OutgoingPayments_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_OutgoingPayments_Status] CHECK ([Status] BETWEEN 0 AND 5),
    CONSTRAINT [CK_OutgoingPayments_Attempts] CHECK ([Attempts] >= 0),
    CONSTRAINT [CK_OutgoingPayments_LastFailure] CHECK ([LastFailure] BETWEEN 0 AND 5),
    CONSTRAINT [CK_OutgoingPayments_RejectReason] CHECK ([RejectReason] BETWEEN 0 AND 3)
);

-- The bank sees the instruction id as the payment's idempotency key (ADR 0021 rule 13): one payment per id.
CREATE UNIQUE INDEX [UX_OutgoingPayments_Instruction] ON [OutgoingPayments] ([InstructionPublicId]);

-- The send step reads the Pending payments of a connection, oldest first (R32).
CREATE INDEX [IX_OutgoingPayments_Connection_Status_Id] ON [OutgoingPayments] ([Connection], [Status], [Id]);

CREATE INDEX [IX_OutgoingPayments_TenantId_WithdrawalId] ON [OutgoingPayments] ([TenantId], [WithdrawalId]);

-- The bank's details of a payout and of its Reversal (R41, R42).
CREATE TABLE [TransactionPaymentDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [ValueDate] date NOT NULL,
    [Reference] nvarchar(18) NULL,
    [PayeeName] nvarchar(140) NULL,
    [PayeeAccount] nvarchar(40) NULL,
    CONSTRAINT [PK_TransactionPaymentDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionPaymentDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionPaymentDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id])
);

CREATE INDEX [IX_TransactionPaymentDetails_TenantId] ON [TransactionPaymentDetails] ([TenantId]);

-- A payout line names its withdrawal by end-to-end id; a returned payout line names the line it returns (R38).
ALTER TABLE [BankStatementLines] ADD
    [EndToEndId] nvarchar(100) NULL,
    [ReturnOfOutsideId] nvarchar(100) NULL,
    [ReturnReason] nvarchar(140) NULL,
    [RelatedLineId] int NULL,
    [WithdrawalId] int NULL;

-- The new columns are referenced inside EXEC, so this batch compiles before they exist.
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [UQ_BankStatementLines_TenantId_Id] UNIQUE ([TenantId], [Id]);
EXEC(N'ALTER TABLE [BankStatementLines] ADD CONSTRAINT [FK_BankStatementLines_BankStatementLines_TenantId_RelatedLineId]
    FOREIGN KEY ([TenantId], [RelatedLineId]) REFERENCES [BankStatementLines] ([TenantId], [Id]);');
EXEC(N'ALTER TABLE [BankStatementLines] ADD CONSTRAINT [FK_BankStatementLines_Withdrawals_TenantId_WithdrawalId]
    FOREIGN KEY ([TenantId], [WithdrawalId]) REFERENCES [Withdrawals] ([TenantId], [Id]);');

ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_UnmatchedReason];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_MatchMethod];
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_UnmatchedReason] CHECK ([UnmatchedReason] BETWEEN 0 AND 7);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_MatchMethod] CHECK ([MatchMethod] BETWEEN 0 AND 3);

-- Processing finds a payout line by its end-to-end id and a returned payout by the line it returns (R39).
EXEC(N'CREATE INDEX [IX_BankStatementLines_Connection_EndToEndId]
    ON [BankStatementLines] ([Connection], [EndToEndId]) WHERE [EndToEndId] IS NOT NULL;');
EXEC(N'CREATE INDEX [IX_BankStatementLines_Connection_ReturnOfOutsideId]
    ON [BankStatementLines] ([Connection], [ReturnOfOutsideId]) WHERE [ReturnOfOutsideId] IS NOT NULL;');

ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 4);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4) AND [SourcePublicId] IS NOT NULL));
