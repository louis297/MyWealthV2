-- Tenant composite keys (docs/fixes/tenant-composite-keys.md, B2).
-- Every reference between two tenant rows becomes (TenantId, XId) -> (TenantId, Id).
-- The applicator runs this script without a surrounding transaction, so it first checks every
-- reference read-only and stops with one error before it changes anything.

DECLARE @violations TABLE ([ForeignKey] sysname NOT NULL, [Rows] int NOT NULL);

INSERT INTO @violations ([ForeignKey], [Rows])
SELECT N'FK_Users_Users_TenantId_AdviserId', COUNT(*)
FROM [Users] child
WHERE child.[TenantId] IS NOT NULL AND child.[AdviserId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Users] parent WHERE parent.[Id] = child.[AdviserId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_Accounts_Users_TenantId_CustomerId', COUNT(*)
FROM [Accounts] child
WHERE child.[TenantId] IS NOT NULL AND child.[CustomerId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Users] parent WHERE parent.[Id] = child.[CustomerId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_Holdings_Accounts_TenantId_AccountId', COUNT(*)
FROM [Holdings] child
WHERE child.[TenantId] IS NOT NULL AND child.[AccountId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Accounts] parent WHERE parent.[Id] = child.[AccountId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_Holdings_Instruments_TenantId_InstrumentId', COUNT(*)
FROM [Holdings] child
WHERE child.[TenantId] IS NOT NULL AND child.[InstrumentId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Instruments] parent WHERE parent.[Id] = child.[InstrumentId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_Transactions_Transactions_TenantId_OriginalTransactionId', COUNT(*)
FROM [Transactions] child
WHERE child.[TenantId] IS NOT NULL AND child.[OriginalTransactionId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Transactions] parent WHERE parent.[Id] = child.[OriginalTransactionId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_Transactions_Transactions_TenantId_ReversedByTransactionId', COUNT(*)
FROM [Transactions] child
WHERE child.[TenantId] IS NOT NULL AND child.[ReversedByTransactionId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Transactions] parent WHERE parent.[Id] = child.[ReversedByTransactionId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_TransactionCashLegs_Transactions_TenantId_TransactionId', COUNT(*)
FROM [TransactionCashLegs] child
WHERE child.[TenantId] IS NOT NULL AND child.[TransactionId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Transactions] parent WHERE parent.[Id] = child.[TransactionId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_TransactionCashLegs_Accounts_TenantId_AccountId', COUNT(*)
FROM [TransactionCashLegs] child
WHERE child.[TenantId] IS NOT NULL AND child.[AccountId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Accounts] parent WHERE parent.[Id] = child.[AccountId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_TransactionSecurityLegs_Transactions_TenantId_TransactionId', COUNT(*)
FROM [TransactionSecurityLegs] child
WHERE child.[TenantId] IS NOT NULL AND child.[TransactionId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Transactions] parent WHERE parent.[Id] = child.[TransactionId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_TransactionSecurityLegs_Accounts_TenantId_AccountId', COUNT(*)
FROM [TransactionSecurityLegs] child
WHERE child.[TenantId] IS NOT NULL AND child.[AccountId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Accounts] parent WHERE parent.[Id] = child.[AccountId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_TransactionSecurityLegs_Instruments_TenantId_InstrumentId', COUNT(*)
FROM [TransactionSecurityLegs] child
WHERE child.[TenantId] IS NOT NULL AND child.[InstrumentId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Instruments] parent WHERE parent.[Id] = child.[InstrumentId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_TransactionFxDetails_Transactions_TenantId_TransactionId', COUNT(*)
FROM [TransactionFxDetails] child
WHERE child.[TenantId] IS NOT NULL AND child.[TransactionId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Transactions] parent WHERE parent.[Id] = child.[TransactionId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_PortfolioLines_Portfolios_TenantId_PortfolioId', COUNT(*)
FROM [PortfolioLines] child
WHERE child.[TenantId] IS NOT NULL AND child.[PortfolioId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Portfolios] parent WHERE parent.[Id] = child.[PortfolioId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_PortfolioLines_Instruments_TenantId_InstrumentId', COUNT(*)
FROM [PortfolioLines] child
WHERE child.[TenantId] IS NOT NULL AND child.[InstrumentId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Instruments] parent WHERE parent.[Id] = child.[InstrumentId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_AccountPortfolioAssignments_Accounts_TenantId_AccountId', COUNT(*)
FROM [AccountPortfolioAssignments] child
WHERE child.[TenantId] IS NOT NULL AND child.[AccountId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Accounts] parent WHERE parent.[Id] = child.[AccountId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_AccountPortfolioAssignments_Portfolios_TenantId_PortfolioId', COUNT(*)
FROM [AccountPortfolioAssignments] child
WHERE child.[TenantId] IS NOT NULL AND child.[PortfolioId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Portfolios] parent WHERE parent.[Id] = child.[PortfolioId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_ReportExecutions_ReportSchedules_TenantId_ScheduleId', COUNT(*)
FROM [ReportExecutions] child
WHERE child.[TenantId] IS NOT NULL AND child.[ScheduleId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [ReportSchedules] parent WHERE parent.[Id] = child.[ScheduleId] AND parent.[TenantId] = child.[TenantId])
UNION ALL
SELECT N'FK_ReportExecutions_Users_TenantId_TriggeredByUserId', COUNT(*)
FROM [ReportExecutions] child
WHERE child.[TenantId] IS NOT NULL AND child.[TriggeredByUserId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [Users] parent WHERE parent.[Id] = child.[TriggeredByUserId] AND parent.[TenantId] = child.[TenantId]);

DECLARE @found nvarchar(2000) = (
    SELECT STRING_AGG([ForeignKey] + N'=' + CAST([Rows] AS nvarchar(20)), N', ')
    FROM @violations
    WHERE [Rows] > 0);

IF @found IS NOT NULL
BEGIN
    DECLARE @message nvarchar(2048) = N'0028_tenant_composite_keys: cross-tenant references found: ' + @found;
    THROW 50000, @message, 1;
END;

-- Parent keys.
ALTER TABLE [Users] ADD CONSTRAINT [UQ_Users_TenantId_Id] UNIQUE ([TenantId], [Id]);
ALTER TABLE [Accounts] ADD CONSTRAINT [UQ_Accounts_TenantId_Id] UNIQUE ([TenantId], [Id]);
ALTER TABLE [Instruments] ADD CONSTRAINT [UQ_Instruments_TenantId_Id] UNIQUE ([TenantId], [Id]);
ALTER TABLE [Transactions] ADD CONSTRAINT [UQ_Transactions_TenantId_Id] UNIQUE ([TenantId], [Id]);
ALTER TABLE [Portfolios] ADD CONSTRAINT [UQ_Portfolios_TenantId_Id] UNIQUE ([TenantId], [Id]);
ALTER TABLE [ReportSchedules] ADD CONSTRAINT [UQ_ReportSchedules_TenantId_Id] UNIQUE ([TenantId], [Id]);

-- Single-column references between tenant rows.
ALTER TABLE [Users] DROP CONSTRAINT [FK_Users_Users_AdviserId];
ALTER TABLE [Accounts] DROP CONSTRAINT [FK_Accounts_Users_CustomerId];
ALTER TABLE [Holdings] DROP CONSTRAINT [FK_Holdings_Accounts_AccountId];
ALTER TABLE [Holdings] DROP CONSTRAINT [FK_Holdings_Instruments_InstrumentId];
ALTER TABLE [Transactions] DROP CONSTRAINT [FK_Transactions_Transactions_OriginalTransactionId];
ALTER TABLE [Transactions] DROP CONSTRAINT [FK_Transactions_Transactions_ReversedByTransactionId];
ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [FK_TransactionCashLegs_Transactions_TransactionId];
ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [FK_TransactionCashLegs_Accounts_AccountId];
ALTER TABLE [TransactionSecurityLegs] DROP CONSTRAINT [FK_TransactionSecurityLegs_Transactions_TransactionId];
ALTER TABLE [TransactionSecurityLegs] DROP CONSTRAINT [FK_TransactionSecurityLegs_Accounts_AccountId];
ALTER TABLE [TransactionSecurityLegs] DROP CONSTRAINT [FK_TransactionSecurityLegs_Instruments_InstrumentId];
ALTER TABLE [TransactionFxDetails] DROP CONSTRAINT [FK_TransactionFxDetails_Transactions_TransactionId];
ALTER TABLE [PortfolioLines] DROP CONSTRAINT [FK_PortfolioLines_Portfolios_PortfolioId];
ALTER TABLE [PortfolioLines] DROP CONSTRAINT [FK_PortfolioLines_Instruments_InstrumentId];
ALTER TABLE [AccountPortfolioAssignments] DROP CONSTRAINT [FK_AccountPortfolioAssignments_Accounts_AccountId];
ALTER TABLE [AccountPortfolioAssignments] DROP CONSTRAINT [FK_AccountPortfolioAssignments_Portfolios_PortfolioId];
ALTER TABLE [ReportExecutions] DROP CONSTRAINT [FK_ReportExecutions_ReportSchedules_ScheduleId];
ALTER TABLE [ReportExecutions] DROP CONSTRAINT [FK_ReportExecutions_Users_TriggeredByUserId];

-- Tenant composite foreign keys (RESTRICT, like the keys they replace).
ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_Users_TenantId_AdviserId]
    FOREIGN KEY ([TenantId], [AdviserId]) REFERENCES [Users] ([TenantId], [Id]);
ALTER TABLE [Accounts] ADD CONSTRAINT [FK_Accounts_Users_TenantId_CustomerId]
    FOREIGN KEY ([TenantId], [CustomerId]) REFERENCES [Users] ([TenantId], [Id]);
ALTER TABLE [Holdings] ADD CONSTRAINT [FK_Holdings_Accounts_TenantId_AccountId]
    FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]);
ALTER TABLE [Holdings] ADD CONSTRAINT [FK_Holdings_Instruments_TenantId_InstrumentId]
    FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]);
ALTER TABLE [Transactions] ADD CONSTRAINT [FK_Transactions_Transactions_TenantId_OriginalTransactionId]
    FOREIGN KEY ([TenantId], [OriginalTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]);
ALTER TABLE [Transactions] ADD CONSTRAINT [FK_Transactions_Transactions_TenantId_ReversedByTransactionId]
    FOREIGN KEY ([TenantId], [ReversedByTransactionId]) REFERENCES [Transactions] ([TenantId], [Id]);
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [FK_TransactionCashLegs_Transactions_TenantId_TransactionId]
    FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]);
ALTER TABLE [TransactionCashLegs] ADD CONSTRAINT [FK_TransactionCashLegs_Accounts_TenantId_AccountId]
    FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]);
ALTER TABLE [TransactionSecurityLegs] ADD CONSTRAINT [FK_TransactionSecurityLegs_Transactions_TenantId_TransactionId]
    FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]);
ALTER TABLE [TransactionSecurityLegs] ADD CONSTRAINT [FK_TransactionSecurityLegs_Accounts_TenantId_AccountId]
    FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]);
ALTER TABLE [TransactionSecurityLegs] ADD CONSTRAINT [FK_TransactionSecurityLegs_Instruments_TenantId_InstrumentId]
    FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]);
ALTER TABLE [TransactionFxDetails] ADD CONSTRAINT [FK_TransactionFxDetails_Transactions_TenantId_TransactionId]
    FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]);
ALTER TABLE [PortfolioLines] ADD CONSTRAINT [FK_PortfolioLines_Portfolios_TenantId_PortfolioId]
    FOREIGN KEY ([TenantId], [PortfolioId]) REFERENCES [Portfolios] ([TenantId], [Id]);
ALTER TABLE [PortfolioLines] ADD CONSTRAINT [FK_PortfolioLines_Instruments_TenantId_InstrumentId]
    FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]);
ALTER TABLE [AccountPortfolioAssignments] ADD CONSTRAINT [FK_AccountPortfolioAssignments_Accounts_TenantId_AccountId]
    FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]);
ALTER TABLE [AccountPortfolioAssignments] ADD CONSTRAINT [FK_AccountPortfolioAssignments_Portfolios_TenantId_PortfolioId]
    FOREIGN KEY ([TenantId], [PortfolioId]) REFERENCES [Portfolios] ([TenantId], [Id]);
ALTER TABLE [ReportExecutions] ADD CONSTRAINT [FK_ReportExecutions_ReportSchedules_TenantId_ScheduleId]
    FOREIGN KEY ([TenantId], [ScheduleId]) REFERENCES [ReportSchedules] ([TenantId], [Id]);
ALTER TABLE [ReportExecutions] ADD CONSTRAINT [FK_ReportExecutions_Users_TenantId_TriggeredByUserId]
    FOREIGN KEY ([TenantId], [TriggeredByUserId]) REFERENCES [Users] ([TenantId], [Id]);
