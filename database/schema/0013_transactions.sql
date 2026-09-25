CREATE TABLE [Transactions] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [Type] int NOT NULL,
    [BookedAt] datetimeoffset NOT NULL,
    [Memo] nvarchar(200) NULL,
    [Reference] nvarchar(100) NULL,
    [OriginalTransactionId] int NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Transactions] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Transactions_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_Transactions_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Transactions_Accounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_Transactions_Transactions_OriginalTransactionId]
        FOREIGN KEY ([OriginalTransactionId]) REFERENCES [Transactions] ([Id]),
    CONSTRAINT [CK_Transactions_Type] CHECK ([Type] BETWEEN 0 AND 8)
);

CREATE INDEX [IX_Transactions_TenantId_AccountId_BookedAt]
    ON [Transactions] ([TenantId], [AccountId], [BookedAt]);

CREATE TABLE [TransactionCashLegs] (
    [Id] int NOT NULL IDENTITY,
    [TransactionId] int NOT NULL,
    [Amount] decimal(18,4) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_TransactionCashLegs] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_TransactionCashLegs_TransactionId] UNIQUE ([TransactionId]),
    CONSTRAINT [FK_TransactionCashLegs_Transactions_TransactionId]
        FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]),
    CONSTRAINT [FK_TransactionCashLegs_Currencies_Currency]
        FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code])
);

CREATE TABLE [IdempotencyRecords] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [Key] uniqueidentifier NOT NULL,
    [Method] nvarchar(10) NOT NULL,
    [Path] nvarchar(200) NOT NULL,
    [RequestHash] nvarchar(64) NOT NULL,
    [ResponseStatus] int NOT NULL,
    [ResponseBody] nvarchar(max) NOT NULL,
    [Created] datetimeoffset NOT NULL,
    CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_IdempotencyRecords_TenantId_Key] UNIQUE ([TenantId], [Key]),
    CONSTRAINT [FK_IdempotencyRecords_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
);
