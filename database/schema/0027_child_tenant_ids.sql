-- Child tenant ids (docs/fixes/tenant-composite-keys.md, B1).
-- Every child table of a tenant row carries TenantId, back-filled from its parent row.
-- Statements that name the new column run inside EXEC so they compile after the column exists.

ALTER TABLE [TransactionCashLegs] ADD [TenantId] int NULL;
ALTER TABLE [TransactionSecurityLegs] ADD [TenantId] int NULL;
ALTER TABLE [TransactionFxDetails] ADD [TenantId] int NULL;
ALTER TABLE [PortfolioLines] ADD [TenantId] int NULL;

EXEC(N'
UPDATE child
SET child.[TenantId] = parent.[TenantId]
FROM [TransactionCashLegs] child
JOIN [Transactions] parent ON parent.[Id] = child.[TransactionId];

UPDATE child
SET child.[TenantId] = parent.[TenantId]
FROM [TransactionSecurityLegs] child
JOIN [Transactions] parent ON parent.[Id] = child.[TransactionId];

UPDATE child
SET child.[TenantId] = parent.[TenantId]
FROM [TransactionFxDetails] child
JOIN [Transactions] parent ON parent.[Id] = child.[TransactionId];

UPDATE child
SET child.[TenantId] = parent.[TenantId]
FROM [PortfolioLines] child
JOIN [Portfolios] parent ON parent.[Id] = child.[PortfolioId];
');

EXEC(N'
ALTER TABLE [TransactionCashLegs] ALTER COLUMN [TenantId] int NOT NULL;
ALTER TABLE [TransactionSecurityLegs] ALTER COLUMN [TenantId] int NOT NULL;
ALTER TABLE [TransactionFxDetails] ALTER COLUMN [TenantId] int NOT NULL;
ALTER TABLE [PortfolioLines] ALTER COLUMN [TenantId] int NOT NULL;
');

EXEC(N'
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [FK_TransactionCashLegs_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]);
ALTER TABLE [TransactionSecurityLegs] ADD CONSTRAINT [FK_TransactionSecurityLegs_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]);
ALTER TABLE [TransactionFxDetails] ADD CONSTRAINT [FK_TransactionFxDetails_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]);
ALTER TABLE [PortfolioLines] ADD CONSTRAINT [FK_PortfolioLines_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]);
');
