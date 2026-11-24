-- In-kind transfers and the custodian feed (docs/features/in-kind-transfers.md §6.1).
-- A safekeeping account is a Tenant's pooled account at a custodian for the securities quoted in one currency. The
-- worker reads its custody movements and the custodian's positions through a custodian connection. An in-kind
-- transfer moves a quantity of one Instrument between a Custodial account and a counterparty; the custody movement
-- that confirms it posts a StockIn or StockOut with source InKindTransfer (10) and an in-kind transfer detail.
-- Forward only, no back-fill: no safekeeping account, movement, transfer or Transaction with source InKindTransfer
-- exists before this script.
-- The applicator runs this script without a surrounding transaction. Statements that name a column added here run
-- inside EXEC, so each batch compiles before the column exists.

-- R3–R5: one row per safekeeping account; never edited, only disabled and enabled.
CREATE TABLE [SafekeepingAccounts] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [CustodianName] nvarchar(100) NOT NULL,
    [AccountName] nvarchar(140) NOT NULL,
    [AccountNumber] nvarchar(34) NOT NULL,
    [CustodianConnection] nvarchar(50) NOT NULL,
    [IsActive] bit NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SafekeepingAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_SafekeepingAccounts_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_SafekeepingAccounts_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_SafekeepingAccounts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_SafekeepingAccounts_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code])
);

-- R5: at most one active safekeeping account per (Tenant, currency).
CREATE UNIQUE INDEX [UX_SafekeepingAccounts_TenantId_Currency_Active]
    ON [SafekeepingAccounts] ([TenantId], [Currency])
    WHERE [IsActive] = 1;

-- R5: an account number is active on at most one row on the platform. The database collation compares it without
-- regard to case.
CREATE UNIQUE INDEX [UX_SafekeepingAccounts_AccountNumber_Active]
    ON [SafekeepingAccounts] ([AccountNumber])
    WHERE [IsActive] = 1;

-- R7, R10, R12: where the feed of one safekeeping account was last read, and when its positions were stored.
CREATE TABLE [CustodianFeedCursors] (
    [SafekeepingAccountId] int NOT NULL,
    [TenantId] int NOT NULL,
    [Cursor] nvarchar(500) NULL,
    [LastSucceededAt] datetimeoffset NULL,
    [LastFailedAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [PositionsAt] datetimeoffset NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CustodianFeedCursors] PRIMARY KEY ([SafekeepingAccountId]),
    CONSTRAINT [FK_CustodianFeedCursors_SafekeepingAccounts_TenantId_SafekeepingAccountId]
        FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]),
    CONSTRAINT [CK_CustodianFeedCursors_LastFailure] CHECK ([LastFailure] IS NULL OR [LastFailure] BETWEEN 0 AND 6)
);

-- R12: the custodian's positions of a safekeeping account, replaced as a set with every stored page.
CREATE TABLE [CustodianPositions] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [SafekeepingAccountId] int NOT NULL,
    [Symbol] nvarchar(32) NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    CONSTRAINT [PK_CustodianPositions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustodianPositions_SafekeepingAccounts_TenantId_SafekeepingAccountId]
        FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]),
    CONSTRAINT [UQ_CustodianPositions_SafekeepingAccountId_Symbol] UNIQUE ([SafekeepingAccountId], [Symbol])
);

-- R16: a custody movement names a fill of the movement's Tenant.
ALTER TABLE [OrderFills] ADD CONSTRAINT [UQ_OrderFills_TenantId_Id] UNIQUE ([TenantId], [Id]);

-- R20–R35: an instruction to move a quantity of one Instrument between one Custodial account and a counterparty.
CREATE TABLE [InKindTransfers] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Direction] int NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Cost] decimal(18, 4) NULL,
    [AcquiredOn] date NULL,
    [ChangesBeneficialOwner] bit NOT NULL,
    [CounterpartyName] nvarchar(100) NOT NULL,
    [CounterpartyAccount] nvarchar(34) NOT NULL,
    [CounterpartyHolderName] nvarchar(140) NOT NULL,
    [Memo] nvarchar(200) NULL,
    [Status] int NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [RequestedAt] datetimeoffset NOT NULL,
    [DecisionAction] int NULL,
    [DecisionByUserId] int NULL,
    [DecisionAt] datetimeoffset NULL,
    [DecisionNote] nvarchar(200) NULL,
    [SafekeepingAccountId] int NULL,
    [Attempts] int NOT NULL,
    [LastAttemptAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [AcceptedAt] datetimeoffset NULL,
    [CustodianTransferId] nvarchar(100) NULL,
    [FailureReason] int NULL,
    [SetAsideByUserId] int NULL,
    [SetAsideAt] datetimeoffset NULL,
    [SetAsideNote] nvarchar(200) NULL,
    [TransactionId] int NULL,
    [CompletedAt] datetimeoffset NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InKindTransfers] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_InKindTransfers_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_InKindTransfers_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_InKindTransfers_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_SafekeepingAccounts_TenantId_SafekeepingAccountId]
        FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_Users_TenantId_RequestedByUserId]
        FOREIGN KEY ([TenantId], [RequestedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_Users_TenantId_DecisionByUserId]
        FOREIGN KEY ([TenantId], [DecisionByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_InKindTransfers_Users_TenantId_SetAsideByUserId]
        FOREIGN KEY ([TenantId], [SetAsideByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_InKindTransfers_Direction] CHECK ([Direction] BETWEEN 0 AND 1),
    CONSTRAINT [CK_InKindTransfers_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_InKindTransfers_Status] CHECK ([Status] BETWEEN 0 AND 7),
    -- R25: a transfer In carries a cost; a transfer Out carries neither a cost nor an acquisition date.
    CONSTRAINT [CK_InKindTransfers_Cost] CHECK (
        ([Direction] = 0 AND [Cost] IS NOT NULL AND [Cost] >= 0)
        OR ([Direction] = 1 AND [Cost] IS NULL AND [AcquiredOn] IS NULL)),
    CONSTRAINT [CK_InKindTransfers_DecisionAction] CHECK ([DecisionAction] IS NULL OR [DecisionAction] BETWEEN 0 AND 2),
    CONSTRAINT [CK_InKindTransfers_Decision] CHECK (
        ([DecisionAction] IS NULL AND [DecisionByUserId] IS NULL AND [DecisionAt] IS NULL AND [DecisionNote] IS NULL)
        OR ([DecisionAction] IS NOT NULL AND [DecisionByUserId] IS NOT NULL AND [DecisionAt] IS NOT NULL)),
    -- R31: an approve or a reject is made by someone other than the requester.
    CONSTRAINT [CK_InKindTransfers_FourEyes] CHECK (
        [DecisionAction] IS NULL OR [DecisionAction] = 2 OR [DecisionByUserId] <> [RequestedByUserId]),
    CONSTRAINT [CK_InKindTransfers_SafekeepingAccount] CHECK (
        [Status] NOT IN (1, 2, 3, 6, 7) OR [SafekeepingAccountId] IS NOT NULL),
    CONSTRAINT [CK_InKindTransfers_Attempts] CHECK ([Attempts] >= 0),
    CONSTRAINT [CK_InKindTransfers_LastFailure] CHECK ([LastFailure] IS NULL OR [LastFailure] BETWEEN 0 AND 5),
    CONSTRAINT [CK_InKindTransfers_FailureReason] CHECK (
        ([Status] = 6 AND [FailureReason] IS NOT NULL AND [FailureReason] BETWEEN 0 AND 3) OR ([Status] <> 6 AND [FailureReason] IS NULL)),
    CONSTRAINT [CK_InKindTransfers_SetAside] CHECK (
        ([Status] = 7 AND [SetAsideByUserId] IS NOT NULL AND [SetAsideAt] IS NOT NULL AND [SetAsideNote] IS NOT NULL)
        OR ([Status] <> 7 AND [SetAsideByUserId] IS NULL AND [SetAsideAt] IS NULL AND [SetAsideNote] IS NULL)),
    -- Invariant: Completed if and only if it has its Transaction.
    CONSTRAINT [CK_InKindTransfers_Completed] CHECK (
        ([Status] = 3 AND [TransactionId] IS NOT NULL AND [CompletedAt] IS NOT NULL)
        OR ([Status] <> 3 AND [TransactionId] IS NULL AND [CompletedAt] IS NULL))
);

CREATE INDEX [IX_InKindTransfers_TenantId_RequestedAt] ON [InKindTransfers] ([TenantId], [RequestedAt] DESC);
CREATE INDEX [IX_InKindTransfers_AccountId_InstrumentId_Status] ON [InKindTransfers] ([AccountId], [InstrumentId], [Status]);
-- R34: the send step reads the Approved transfers of a safekeeping account.
CREATE INDEX [IX_InKindTransfers_SafekeepingAccountId_Approved]
    ON [InKindTransfers] ([SafekeepingAccountId], [Id])
    WHERE [Status] = 1;

-- R8–R19: the custodian's inbound record of one change of its position in one instrument.
CREATE TABLE [CustodyMovements] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [SafekeepingAccountId] int NOT NULL,
    [Connection] nvarchar(50) NOT NULL,
    [OutsideId] nvarchar(100) NOT NULL,
    [Direction] int NOT NULL,
    [Kind] int NULL,
    [Symbol] nvarchar(32) NOT NULL,
    [Currency] char(3) NOT NULL,
    [InstrumentId] int NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [SettlementDate] date NOT NULL,
    [InstructionId] nvarchar(100) NULL,
    [TradeReference] nvarchar(100) NULL,
    [CorporateActionReference] nvarchar(100) NULL,
    [CounterpartyName] nvarchar(140) NULL,
    [CounterpartyAccount] nvarchar(34) NULL,
    [Reference] nvarchar(140) NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    [Status] int NOT NULL,
    [MatchMethod] int NULL,
    [UnmatchedReason] int NULL,
    [AccountId] int NULL,
    [TransactionId] int NULL,
    [InKindTransferId] int NULL,
    [OrderFillId] bigint NULL,
    [CorporateActionId] int NULL,
    [LastAction] int NULL,
    [LastActionByUserId] int NULL,
    [LastActionAt] datetimeoffset NULL,
    [LastActionNote] nvarchar(200) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CustodyMovements] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CustodyMovements_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_CustodyMovements_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [UX_CustodyMovements_Connection_OutsideId] UNIQUE ([Connection], [OutsideId]),
    CONSTRAINT [FK_CustodyMovements_SafekeepingAccounts_TenantId_SafekeepingAccountId]
        FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_InKindTransfers_TenantId_InKindTransferId]
        FOREIGN KEY ([TenantId], [InKindTransferId]) REFERENCES [InKindTransfers] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_OrderFills_TenantId_OrderFillId]
        FOREIGN KEY ([TenantId], [OrderFillId]) REFERENCES [OrderFills] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_CorporateActions_TenantId_CorporateActionId]
        FOREIGN KEY ([TenantId], [CorporateActionId]) REFERENCES [CorporateActions] ([TenantId], [Id]),
    CONSTRAINT [FK_CustodyMovements_Users_TenantId_LastActionByUserId]
        FOREIGN KEY ([TenantId], [LastActionByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_CustodyMovements_Direction] CHECK ([Direction] BETWEEN 0 AND 1),
    CONSTRAINT [CK_CustodyMovements_Kind] CHECK ([Kind] IS NULL OR [Kind] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CustodyMovements_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_CustodyMovements_Status] CHECK ([Status] BETWEEN 0 AND 3),
    CONSTRAINT [CK_CustodyMovements_MatchMethod] CHECK (
        ([Status] = 1 AND [MatchMethod] IS NOT NULL AND [MatchMethod] BETWEEN 0 AND 2) OR ([Status] <> 1 AND [MatchMethod] IS NULL)),
    CONSTRAINT [CK_CustodyMovements_UnmatchedReason] CHECK (
        ([Status] IN (2, 3) AND [UnmatchedReason] IS NOT NULL AND [UnmatchedReason] BETWEEN 0 AND 9)
        OR ([Status] NOT IN (2, 3) AND [UnmatchedReason] IS NULL)),
    -- R36, R16, R17: each match method has its own links and no other.
    CONSTRAINT [CK_CustodyMovements_Match] CHECK (
        ([MatchMethod] = 0 AND [AccountId] IS NOT NULL AND [TransactionId] IS NOT NULL AND [InKindTransferId] IS NOT NULL
            AND [OrderFillId] IS NULL AND [CorporateActionId] IS NULL)
        OR ([MatchMethod] = 1 AND [OrderFillId] IS NOT NULL AND [AccountId] IS NULL AND [TransactionId] IS NULL
            AND [InKindTransferId] IS NULL AND [CorporateActionId] IS NULL)
        OR ([MatchMethod] = 2 AND [CorporateActionId] IS NOT NULL AND [AccountId] IS NULL AND [TransactionId] IS NULL
            AND [InKindTransferId] IS NULL AND [OrderFillId] IS NULL)
        OR ([MatchMethod] IS NULL AND [AccountId] IS NULL AND [TransactionId] IS NULL AND [InKindTransferId] IS NULL
            AND [OrderFillId] IS NULL AND [CorporateActionId] IS NULL)),
    CONSTRAINT [CK_CustodyMovements_LastAction] CHECK (
        ([LastAction] IS NULL AND [LastActionByUserId] IS NULL AND [LastActionAt] IS NULL AND [LastActionNote] IS NULL)
        OR ([LastAction] IS NOT NULL AND [LastAction] BETWEEN 0 AND 1 AND [LastActionByUserId] IS NOT NULL AND [LastActionAt] IS NOT NULL))
);

CREATE INDEX [IX_CustodyMovements_Connection_Status_ReceivedAt]
    ON [CustodyMovements] ([Connection], [Status], [ReceivedAt]);
CREATE INDEX [IX_CustodyMovements_TenantId_Status_ReceivedAt]
    ON [CustodyMovements] ([TenantId], [Status], [ReceivedAt] DESC);
CREATE INDEX [IX_CustodyMovements_SafekeepingAccountId_Symbol_Status]
    ON [CustodyMovements] ([SafekeepingAccountId], [Symbol], [Status]);
-- R16: a fill has at most one movement matched to it.
CREATE UNIQUE INDEX [UX_CustodyMovements_OrderFillId]
    ON [CustodyMovements] ([OrderFillId])
    WHERE [OrderFillId] IS NOT NULL;
CREATE INDEX [IX_CustodyMovements_InKindTransferId]
    ON [CustodyMovements] ([InKindTransferId])
    WHERE [InKindTransferId] IS NOT NULL;

-- R38: the transfer behind a StockIn or StockOut; at most one row per Transaction.
CREATE TABLE [TransactionInKindTransferDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [TransferPublicId] uniqueidentifier NOT NULL,
    [CounterpartyName] nvarchar(100) NOT NULL,
    [CounterpartyAccountMasked] nvarchar(34) NOT NULL,
    [ChangesBeneficialOwner] bit NOT NULL,
    [AcquiredOn] date NULL,
    [SettlementDate] date NOT NULL,
    [CustodianReference] nvarchar(100) NOT NULL,
    CONSTRAINT [PK_TransactionInKindTransferDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionInKindTransferDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id])
);

CREATE INDEX [IX_TransactionInKindTransferDetails_TenantId] ON [TransactionInKindTransferDetails] ([TenantId]);

-- R46: the safekeeping account an order was placed with; both columns or neither.
ALTER TABLE [Orders] ADD [SafekeepingAccountId] int NULL, [SafekeepingAccountNumber] nvarchar(34) NULL;
EXEC(N'
ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_SafekeepingAccounts_TenantId_SafekeepingAccountId]
    FOREIGN KEY ([TenantId], [SafekeepingAccountId]) REFERENCES [SafekeepingAccounts] ([TenantId], [Id]);
ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_SafekeepingAccount] CHECK (
    ([SafekeepingAccountId] IS NULL AND [SafekeepingAccountNumber] IS NULL)
    OR ([SafekeepingAccountId] IS NOT NULL AND [SafekeepingAccountNumber] IS NOT NULL));
');

-- R49: Transaction source kind InKindTransfer = 10, with the transfer's PublicId as source id.
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 10);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5, 6, 7, 8, 9, 10) AND [SourcePublicId] IS NOT NULL));
