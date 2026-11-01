CREATE TABLE [OutboxMessages] (
    [Id] uniqueidentifier NOT NULL,
    [Type] nvarchar(200) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [TenantId] int NULL,
    [Status] nvarchar(20) NOT NULL,
    [ProcessedAt] datetimeoffset NULL,
    [Error] nvarchar(2000) NULL,
    [RetryCount] int NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_OutboxMessages_Status] CHECK ([Status] IN (
        N'Pending', N'Processing', N'Processed', N'Failed', N'DeadLettered'))
);

CREATE INDEX [IX_OutboxMessages_Status_OccurredAt]
    ON [OutboxMessages] ([Status], [OccurredAt]);
