CREATE TABLE [Instruments] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [Symbol] nvarchar(32) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [QuoteCurrency] char(3) NOT NULL,
    [IsEnabled] bit NOT NULL CONSTRAINT [DF_Instruments_IsEnabled] DEFAULT (1),
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Instruments] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Instruments_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Instruments_TenantId_Symbol] UNIQUE ([TenantId], [Symbol]),
    CONSTRAINT [FK_Instruments_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Instruments_Currencies_QuoteCurrency]
        FOREIGN KEY ([QuoteCurrency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_Instruments_Symbol] CHECK (
        LEN([Symbol]) BETWEEN 1 AND 32
        AND [Symbol] NOT LIKE N'%[^A-Z0-9.-]%'
    )
);

CREATE INDEX [IX_Instruments_TenantId_IsEnabled] ON [Instruments] ([TenantId], [IsEnabled]);
