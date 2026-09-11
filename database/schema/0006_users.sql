CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NULL,
    [Name] nvarchar(200) NOT NULL,
    [Email] nvarchar(256) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Role] int NOT NULL,
    [Status] int NOT NULL,
    [AdviserId] int NULL,
    [IdentityUserId] nvarchar(450) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NULL,
    [LastModified] datetimeoffset NOT NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Users_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Users_IdentityUserId] UNIQUE ([IdentityUserId]),
    CONSTRAINT [FK_Users_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_Users_Users_AdviserId] FOREIGN KEY ([AdviserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_Users_AspNetUsers_IdentityUserId] FOREIGN KEY ([IdentityUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [CK_Users_RoleShape] CHECK (
        ([Role] = 0 AND [TenantId] IS NULL AND [AdviserId] IS NULL)
        OR ([Role] IN (1, 2) AND [TenantId] IS NOT NULL AND [AdviserId] IS NULL)
        OR ([Role] = 3 AND [TenantId] IS NOT NULL AND [AdviserId] IS NOT NULL)
    )
);

CREATE UNIQUE INDEX [UQ_Users_Tenant_Email] ON [Users] ([TenantId], [Email]) WHERE [TenantId] IS NOT NULL;
CREATE UNIQUE INDEX [UQ_Users_SystemAdmin_Email] ON [Users] ([Email]) WHERE [TenantId] IS NULL;
CREATE INDEX [IX_Users_TenantId_Role] ON [Users] ([TenantId], [Role]);
CREATE INDEX [IX_Users_AdviserId] ON [Users] ([AdviserId]);
CREATE INDEX [IX_Users_Status] ON [Users] ([Status]);
