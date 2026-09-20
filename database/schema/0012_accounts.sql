CREATE TABLE [Accounts] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [CustomerId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Type] int NOT NULL,
    [Status] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [IsActive] AS ISNULL(CAST(CASE WHEN [Status] = 0 THEN 1 ELSE 0 END AS bit), 0) PERSISTED,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Accounts_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_Accounts_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Accounts_Users_CustomerId]
        FOREIGN KEY ([CustomerId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_Accounts_Currencies_Currency]
        FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_Accounts_Type] CHECK ([Type] BETWEEN 0 AND 5),
    CONSTRAINT [CK_Accounts_Status] CHECK ([Status] BETWEEN 0 AND 1)
);

CREATE INDEX [IX_Accounts_TenantId_CustomerId] ON [Accounts] ([TenantId], [CustomerId]);
CREATE INDEX [IX_Accounts_TenantId_IsActive] ON [Accounts] ([TenantId], [IsActive]);
