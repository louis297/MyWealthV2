-- Client money accounts (docs/features/client-money-accounts.md §6.1).
-- The trust bank account a Tenant holds Customers' cash in, one row per (Tenant, currency) while active.
-- The four details never change; a correction disables the row and adds a new one. Forward only.

CREATE TABLE [ClientMoneyAccounts] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [Currency] char(3) NOT NULL,
    [BankName] nvarchar(100) NOT NULL,
    [AccountName] nvarchar(140) NOT NULL,
    [AccountNumber] nvarchar(34) NOT NULL,
    [IsActive] bit NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ClientMoneyAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_ClientMoneyAccounts_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [FK_ClientMoneyAccounts_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_ClientMoneyAccounts_Currencies_Currency]
        FOREIGN KEY ([Currency]) REFERENCES [Currencies] ([Code])
);

-- At most one active client money account per (Tenant, currency) (R4).
CREATE UNIQUE INDEX [UX_ClientMoneyAccounts_TenantId_Currency_Active]
    ON [ClientMoneyAccounts] ([TenantId], [Currency])
    WHERE [IsActive] = 1;

-- An account number is active on at most one row on the platform (R5). The database collation compares it
-- without regard to case.
CREATE UNIQUE INDEX [UX_ClientMoneyAccounts_AccountNumber_Active]
    ON [ClientMoneyAccounts] ([AccountNumber])
    WHERE [IsActive] = 1;

CREATE INDEX [IX_ClientMoneyAccounts_TenantId] ON [ClientMoneyAccounts] ([TenantId]);
