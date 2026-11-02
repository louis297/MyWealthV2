CREATE TABLE [Portfolios] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [BaseCurrency] char(3) NOT NULL,
    [IsActive] bit NOT NULL,
    [DriftAbsolute] decimal(9, 4) NOT NULL CONSTRAINT [DF_Portfolios_DriftAbsolute] DEFAULT (1),
    [DriftRelative] decimal(9, 4) NOT NULL CONSTRAINT [DF_Portfolios_DriftRelative] DEFAULT (5),
    [MinimumTrade] decimal(18, 4) NOT NULL CONSTRAINT [DF_Portfolios_MinimumTrade] DEFAULT (100),
    [CashResidualPercent] decimal(9, 4) NOT NULL CONSTRAINT [DF_Portfolios_CashResidualPercent] DEFAULT (1),
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Portfolios] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Portfolios_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_Portfolios_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Portfolios_Currencies_BaseCurrency]
        FOREIGN KEY ([BaseCurrency]) REFERENCES [Currencies] ([Code])
);

CREATE TABLE [PortfolioLines] (
    [Id] int NOT NULL IDENTITY,
    [PortfolioId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [TargetWeight] decimal(9, 2) NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_PortfolioLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PortfolioLines_Portfolios_PortfolioId]
        FOREIGN KEY ([PortfolioId]) REFERENCES [Portfolios] ([Id]),
    CONSTRAINT [FK_PortfolioLines_Instruments_InstrumentId]
        FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Id]),
    CONSTRAINT [UQ_PortfolioLines_PortfolioId_InstrumentId] UNIQUE ([PortfolioId], [InstrumentId]),
    CONSTRAINT [CK_PortfolioLines_TargetWeight] CHECK ([TargetWeight] > 0 AND [TargetWeight] <= 100)
);

CREATE TABLE [AccountPortfolioAssignments] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [AccountId] int NOT NULL,
    [PortfolioId] int NOT NULL,
    [Status] int NOT NULL,
    [AssignedAt] datetimeoffset NOT NULL,
    [AssignedBy] nvarchar(450) NOT NULL,
    [LastRebalanceAt] datetimeoffset NULL,
    [LastRebalanceStatus] int NULL,
    [LastRebalanceMessage] nvarchar(500) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_AccountPortfolioAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_AccountPortfolioAssignments_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_AccountPortfolioAssignments_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_AccountPortfolioAssignments_Accounts_AccountId]
        FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]),
    CONSTRAINT [FK_AccountPortfolioAssignments_Portfolios_PortfolioId]
        FOREIGN KEY ([PortfolioId]) REFERENCES [Portfolios] ([Id]),
    CONSTRAINT [CK_AccountPortfolioAssignments_Status] CHECK ([Status] IN (0, 1)),
    CONSTRAINT [CK_AccountPortfolioAssignments_LastRebalanceStatus]
        CHECK ([LastRebalanceStatus] IS NULL OR [LastRebalanceStatus] IN (0, 1, 2))
);

CREATE UNIQUE INDEX [IX_AccountPortfolioAssignments_AccountId_Active]
    ON [AccountPortfolioAssignments] ([AccountId])
    WHERE [Status] = 0;

CREATE INDEX [IX_Portfolios_TenantId_IsActive] ON [Portfolios] ([TenantId], [IsActive]);
CREATE INDEX [IX_AccountPortfolioAssignments_TenantId] ON [AccountPortfolioAssignments] ([TenantId]);
