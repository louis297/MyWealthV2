-- Nominated bank accounts (docs/features/withdrawals.md §6.1).
-- A Customer's own bank accounts that withdrawals may be paid to. The details never change (R6); a TenantAdmin
-- other than the person who added a row verifies or rejects it, and a row is removed with a note. Forward only.

CREATE TABLE [NominatedBankAccounts] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [CustomerId] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [BankName] nvarchar(100) NOT NULL,
    [AccountName] nvarchar(140) NOT NULL,
    [AccountNumber] nvarchar(34) NOT NULL,
    [AccountNumberKey] nvarchar(34) NOT NULL,
    [Status] int NOT NULL,
    [AddedByUserId] int NOT NULL,
    [AddedAt] datetimeoffset NOT NULL,
    [ReviewedByUserId] int NULL,
    [ReviewedAt] datetimeoffset NULL,
    [ReviewNote] nvarchar(200) NULL,
    [RemovedByUserId] int NULL,
    [RemovedAt] datetimeoffset NULL,
    [RemovedNote] nvarchar(200) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_NominatedBankAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_NominatedBankAccounts_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_NominatedBankAccounts_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_NominatedBankAccounts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_NominatedBankAccounts_Currencies_Currency] FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_NominatedBankAccounts_Users_TenantId_CustomerId]
        FOREIGN KEY ([TenantId], [CustomerId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_NominatedBankAccounts_Users_TenantId_AddedByUserId]
        FOREIGN KEY ([TenantId], [AddedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_NominatedBankAccounts_Users_TenantId_ReviewedByUserId]
        FOREIGN KEY ([TenantId], [ReviewedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_NominatedBankAccounts_Users_TenantId_RemovedByUserId]
        FOREIGN KEY ([TenantId], [RemovedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_NominatedBankAccounts_Status] CHECK ([Status] BETWEEN 0 AND 3),
    -- The review is set together or not at all: set when Verified or Rejected, null when Pending, either when
    -- Removed (removed after or before a verify).
    CONSTRAINT [CK_NominatedBankAccounts_Review] CHECK (
        ([ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewNote] IS NULL AND [Status] IN (0, 3))
        OR ([ReviewedByUserId] IS NOT NULL AND [ReviewedAt] IS NOT NULL AND [ReviewNote] IS NOT NULL AND [Status] IN (1, 2, 3))),
    -- The removal is set if and only if the row is Removed.
    CONSTRAINT [CK_NominatedBankAccounts_Removal] CHECK (
        ([Status] = 3 AND [RemovedByUserId] IS NOT NULL AND [RemovedAt] IS NOT NULL AND [RemovedNote] IS NOT NULL)
        OR ([Status] <> 3 AND [RemovedByUserId] IS NULL AND [RemovedAt] IS NULL AND [RemovedNote] IS NULL))
);

-- R5: one Pending or Verified row per Customer, account number and currency.
CREATE UNIQUE INDEX [UX_NominatedBankAccounts_Customer_Number]
    ON [NominatedBankAccounts] ([TenantId], [CustomerId], [AccountNumberKey], [Currency])
    WHERE [Status] IN (0, 1);

-- The verification list (R13).
CREATE INDEX [IX_NominatedBankAccounts_TenantId_Status_AddedAt]
    ON [NominatedBankAccounts] ([TenantId], [Status], [AddedAt] DESC);
