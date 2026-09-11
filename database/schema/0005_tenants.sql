CREATE TABLE [Tenants] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Code] nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [IsEnabled] bit NOT NULL CONSTRAINT [DF_Tenants_IsEnabled] DEFAULT (1),
    [RowVersion] rowversion NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NULL,
    [LastModified] datetimeoffset NOT NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Tenants_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Tenants_Name] UNIQUE ([Name]),
    CONSTRAINT [UQ_Tenants_Code] UNIQUE ([Code]),
    CONSTRAINT [CK_Tenants_Code] CHECK (LEN([Code]) BETWEEN 2 AND 50)
);

CREATE INDEX [IX_Tenants_IsEnabled] ON [Tenants] ([IsEnabled]);
