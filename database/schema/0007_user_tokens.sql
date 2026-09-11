CREATE TABLE [UserTokens] (
    [Id] int NOT NULL IDENTITY,
    [Purpose] int NOT NULL,
    [TenantId] int NULL,
    [Email] nvarchar(256) NOT NULL,
    [IdentityUserId] nvarchar(450) NULL,
    [TokenHash] nvarchar(128) NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [ConsumedAt] datetimeoffset NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_UserTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_UserTokens_TokenHash] UNIQUE ([TokenHash])
);

CREATE INDEX [IX_UserTokens_TenantId_Email] ON [UserTokens] ([TenantId], [Email]);
CREATE INDEX [IX_UserTokens_IdentityUserId] ON [UserTokens] ([IdentityUserId]);
