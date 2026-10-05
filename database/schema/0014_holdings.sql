CREATE TABLE [TransactionSecurityLegs] (
    [Id] int NOT NULL IDENTITY,
    [TransactionId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Quantity] decimal(18,8) NOT NULL,
    [CostAmount] decimal(18,4) NOT NULL,
    [CostCurrency] char(3) NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_TransactionSecurityLegs] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_TransactionSecurityLegs_TransactionId_InstrumentId] UNIQUE ([TransactionId], [InstrumentId]),
    CONSTRAINT [FK_TransactionSecurityLegs_Transactions_TransactionId]
        FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]),
    CONSTRAINT [FK_TransactionSecurityLegs_Instruments_InstrumentId]
        FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Id]),
    CONSTRAINT [FK_TransactionSecurityLegs_Currencies_CostCurrency]
        FOREIGN KEY ([CostCurrency]) REFERENCES [Currencies] ([Code])
);

CREATE TABLE [Holdings] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Quantity] decimal(18,8) NOT NULL,
    [CostAmount] decimal(18,4) NOT NULL,
    [CostCurrency] char(3) NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Holdings] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Holdings_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Holdings_AccountId_InstrumentId] UNIQUE ([AccountId], [InstrumentId]),
    CONSTRAINT [FK_Holdings_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Holdings_Accounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_Holdings_Instruments_InstrumentId]
        FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Id]),
    CONSTRAINT [FK_Holdings_Currencies_CostCurrency]
        FOREIGN KEY ([CostCurrency]) REFERENCES [Currencies] ([Code])
);
