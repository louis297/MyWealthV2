CREATE TABLE [Reports] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Function] nvarchar(500) NOT NULL,
    [Scope] nvarchar(20) NOT NULL,
    [AllowedTriggers] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_Reports_IsActive] DEFAULT (1),
    CONSTRAINT [PK_Reports] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_Reports_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_Reports_Code] UNIQUE ([Code]),
    CONSTRAINT [CK_Reports_Scope] CHECK ([Scope] IN (N'Customer', N'Book'))
);

CREATE TABLE [TenantReports] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [ReportId] int NOT NULL,
    CONSTRAINT [PK_TenantReports] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_TenantReports_TenantId_ReportId] UNIQUE ([TenantId], [ReportId]),
    CONSTRAINT [FK_TenantReports_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TenantReports_Reports_ReportId]
        FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id])
);

CREATE TABLE [ReportSchedules] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [ReportId] int NOT NULL,
    [TenantId] int NOT NULL,
    [ParametersJson] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [Cron] nvarchar(100) NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReportSchedules] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_ReportSchedules_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_ReportSchedules_Reports_ReportId]
        FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]),
    CONSTRAINT [FK_ReportSchedules_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
);

CREATE INDEX [IX_ReportSchedules_TenantId_ReportId]
    ON [ReportSchedules] ([TenantId], [ReportId]);

CREATE TABLE [ReportExecutions] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [ReportId] int NOT NULL,
    [ScheduleId] int NULL,
    [TenantId] int NOT NULL,
    [TriggerKind] nvarchar(20) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [ParametersJson] nvarchar(max) NOT NULL,
    [AsOf] datetimeoffset NULL,
    [TriggeredByUserId] int NULL,
    [ResultStorageKey] nvarchar(500) NULL,
    [ErrorMessage] nvarchar(2000) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ReportExecutions] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_ReportExecutions_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_ReportExecutions_Reports_ReportId]
        FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]),
    CONSTRAINT [FK_ReportExecutions_ReportSchedules_ScheduleId]
        FOREIGN KEY ([ScheduleId]) REFERENCES [ReportSchedules] ([Id]),
    CONSTRAINT [FK_ReportExecutions_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_ReportExecutions_Users_TriggeredByUserId]
        FOREIGN KEY ([TriggeredByUserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [CK_ReportExecutions_TriggerKind] CHECK ([TriggerKind] IN (N'Manual', N'Scheduled', N'Event')),
    CONSTRAINT [CK_ReportExecutions_Status] CHECK ([Status] IN (N'Queued', N'Running', N'Succeeded', N'Failed'))
);

CREATE INDEX [IX_ReportExecutions_TenantId_ReportId]
    ON [ReportExecutions] ([TenantId], [ReportId]);

INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers], [IsActive]) VALUES
    ('5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22', N'adviser-balances', N'Adviser Balances Report', N'Point-in-time cash and holding lines for an adviser''s customers.', N'Book', N'["manual","scheduled"]', 1),
    ('8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11', N'customer-transactions', N'Customer Transactions Report', N'Transactions in a date range for one customer.', N'Customer', N'["manual","scheduled"]', 1);
