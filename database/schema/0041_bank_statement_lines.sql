-- Bank statement lines (docs/features/deposit-matching.md §6.2).
-- The bank's inbound records, stored once per (connection, outside id) before they are used (ADR 0021 rule 7),
-- the cursor and feed status of each client money account, and the bank receipt detail of each deposit and its
-- Reversal. Widens the Transaction source checks for BankFeed (3), which always has a source id (the statement
-- line's PublicId). Forward only; no existing row changes.

CREATE TABLE [BankFeedCursors] (
    [ClientMoneyAccountId] int NOT NULL,
    [Cursor] nvarchar(500) NULL,
    [LastSucceededAt] datetimeoffset NULL,
    [LastFailedAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BankFeedCursors] PRIMARY KEY ([ClientMoneyAccountId]),
    CONSTRAINT [FK_BankFeedCursors_ClientMoneyAccounts_ClientMoneyAccountId]
        FOREIGN KEY ([ClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([Id]),
    CONSTRAINT [CK_BankFeedCursors_LastFailure] CHECK ([LastFailure] BETWEEN 0 AND 6)
);

CREATE TABLE [BankStatementLines] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [ClientMoneyAccountId] int NOT NULL,
    [Connection] nvarchar(50) NOT NULL,
    [OutsideId] nvarchar(100) NOT NULL,
    [ValueDate] date NOT NULL,
    [Direction] int NOT NULL,
    [Amount] decimal(18, 4) NOT NULL,
    [Currency] char(3) NOT NULL,
    [PayerName] nvarchar(140) NULL,
    [PayerAccountMasked] nvarchar(40) NULL,
    [ReferenceText] nvarchar(140) NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    [Status] int NOT NULL,
    [UnmatchedReason] int NULL,
    [MatchedAccountId] int NULL,
    [MatchedTransactionId] int NULL,
    [MatchMethod] int NULL,
    [MatchedAt] datetimeoffset NULL,
    [LastAction] int NULL,
    [LastActionNote] nvarchar(200) NULL,
    [LastActionByUserId] int NULL,
    [LastActionAt] datetimeoffset NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BankStatementLines] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_BankStatementLines_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_BankStatementLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_BankStatementLines_ClientMoneyAccounts_ClientMoneyAccountId]
        FOREIGN KEY ([ClientMoneyAccountId]) REFERENCES [ClientMoneyAccounts] ([Id]),
    CONSTRAINT [FK_BankStatementLines_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_BankStatementLines_Accounts_TenantId_MatchedAccountId]
        FOREIGN KEY ([TenantId], [MatchedAccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_BankStatementLines_Transactions_TenantId_MatchedTransactionId]
        FOREIGN KEY ([TenantId], [MatchedTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_BankStatementLines_Users_TenantId_LastActionByUserId]
        FOREIGN KEY ([TenantId], [LastActionByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_BankStatementLines_Direction] CHECK ([Direction] BETWEEN 0 AND 1),
    CONSTRAINT [CK_BankStatementLines_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_BankStatementLines_Status] CHECK ([Status] BETWEEN 0 AND 4),
    CONSTRAINT [CK_BankStatementLines_UnmatchedReason] CHECK ([UnmatchedReason] BETWEEN 0 AND 6),
    CONSTRAINT [CK_BankStatementLines_MatchMethod] CHECK ([MatchMethod] BETWEEN 0 AND 1),
    CONSTRAINT [CK_BankStatementLines_LastAction] CHECK ([LastAction] BETWEEN 0 AND 4),
    -- A Matched line has an account, a deposit, a method and a time; no other status has any of them.
    CONSTRAINT [CK_BankStatementLines_Match] CHECK (
        ([Status] = 1 AND [MatchedAccountId] IS NOT NULL AND [MatchedTransactionId] IS NOT NULL
            AND [MatchMethod] IS NOT NULL AND [MatchedAt] IS NOT NULL)
        OR ([Status] <> 1 AND [MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL
            AND [MatchMethod] IS NULL AND [MatchedAt] IS NULL))
);

-- A line is stored once per connection (R8): a second copy of the same outside id is rejected.
CREATE UNIQUE INDEX [UX_BankStatementLines_Connection_OutsideId]
    ON [BankStatementLines] ([Connection], [OutsideId]);

-- Suspense lists (R22).
CREATE INDEX [IX_BankStatementLines_TenantId_Status_ReceivedAt]
    ON [BankStatementLines] ([TenantId], [Status], [ReceivedAt] DESC);

-- Processing reads the Received lines of a connection, oldest first (R13).
CREATE INDEX [IX_BankStatementLines_Connection_Status_ReceivedAt]
    ON [BankStatementLines] ([Connection], [Status], [ReceivedAt]);

CREATE INDEX [IX_BankStatementLines_ClientMoneyAccountId] ON [BankStatementLines] ([ClientMoneyAccountId]);

-- The bank's details of a deposit and of its Reversal (R32, R33).
CREATE TABLE [TransactionBankReceiptDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [ValueDate] date NOT NULL,
    [Reference] nvarchar(140) NULL,
    [PayerName] nvarchar(140) NULL,
    [PayerAccount] nvarchar(40) NULL,
    CONSTRAINT [PK_TransactionBankReceiptDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionBankReceiptDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionBankReceiptDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id])
);

CREATE INDEX [IX_TransactionBankReceiptDetails_TenantId] ON [TransactionBankReceiptDetails] ([TenantId]);

ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 3);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3) AND [SourcePublicId] IS NOT NULL));
