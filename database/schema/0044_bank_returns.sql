-- Receipt returns, recalls and other debit lines (docs/features/bank-returns.md §6.1).
-- An outgoing payment gains a kind: a withdrawal (0, every existing row) or the return of a receipt (1), which
-- names the statement line it sends back instead of a withdrawal. A line has at most one open return payment.
-- Statement lines gain the statuses ReturnSent (5) and Returned (6), the reasons 8 to 11 and the method Recall
-- (4); a Matched line may have no account and no transaction when it names a related line. Forward only.

ALTER TABLE [OutgoingPayments] ADD [Kind] int NOT NULL CONSTRAINT [DF_OutgoingPayments_Kind] DEFAULT (0);
ALTER TABLE [OutgoingPayments] ADD [ReturnOfLineId] int NULL;

-- WithdrawalId becomes optional; its index and FK are dropped while the column changes.
ALTER TABLE [OutgoingPayments] DROP CONSTRAINT [FK_OutgoingPayments_Withdrawals_TenantId_WithdrawalId];
DROP INDEX [IX_OutgoingPayments_TenantId_WithdrawalId] ON [OutgoingPayments];
ALTER TABLE [OutgoingPayments] ALTER COLUMN [WithdrawalId] int NULL;
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [FK_OutgoingPayments_Withdrawals_TenantId_WithdrawalId]
    FOREIGN KEY ([TenantId], [WithdrawalId]) REFERENCES [Withdrawals] ([TenantId], [Id]);
CREATE INDEX [IX_OutgoingPayments_TenantId_WithdrawalId] ON [OutgoingPayments] ([TenantId], [WithdrawalId]);

ALTER TABLE [OutgoingPayments] DROP CONSTRAINT [CK_OutgoingPayments_RejectReason];
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [CK_OutgoingPayments_RejectReason] CHECK ([RejectReason] BETWEEN 0 AND 4);

-- The new columns are referenced inside EXEC, so this batch compiles before they exist.
EXEC(N'
ALTER TABLE [OutgoingPayments] DROP CONSTRAINT [DF_OutgoingPayments_Kind];
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [CK_OutgoingPayments_Kind] CHECK ([Kind] BETWEEN 0 AND 1);
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [CK_OutgoingPayments_Target] CHECK (
    ([Kind] = 0 AND [WithdrawalId] IS NOT NULL AND [ReturnOfLineId] IS NULL)
    OR ([Kind] = 1 AND [WithdrawalId] IS NULL AND [ReturnOfLineId] IS NOT NULL));
ALTER TABLE [OutgoingPayments] ADD CONSTRAINT [FK_OutgoingPayments_BankStatementLines_TenantId_ReturnOfLineId]
    FOREIGN KEY ([TenantId], [ReturnOfLineId]) REFERENCES [BankStatementLines] ([TenantId], [Id]);
CREATE UNIQUE INDEX [UX_OutgoingPayments_ReturnOfLineId_Open]
    ON [OutgoingPayments] ([ReturnOfLineId]) WHERE [Kind] = 1 AND [Status] IN (0, 1);
');

ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_Status];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_UnmatchedReason];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_MatchMethod];
ALTER TABLE [BankStatementLines] DROP CONSTRAINT [CK_BankStatementLines_Match];
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_Status] CHECK ([Status] BETWEEN 0 AND 6);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_UnmatchedReason] CHECK ([UnmatchedReason] BETWEEN 0 AND 11);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_MatchMethod] CHECK ([MatchMethod] BETWEEN 0 AND 4);
ALTER TABLE [BankStatementLines] ADD CONSTRAINT [CK_BankStatementLines_Match] CHECK (
    ([Status] = 1 AND [MatchMethod] IS NOT NULL AND [MatchedAt] IS NOT NULL
        AND (([MatchedAccountId] IS NOT NULL AND [MatchedTransactionId] IS NOT NULL)
            OR ([MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL AND [RelatedLineId] IS NOT NULL)))
    OR ([Status] <> 1 AND [MatchedAccountId] IS NULL AND [MatchedTransactionId] IS NULL
        AND [MatchMethod] IS NULL AND [MatchedAt] IS NULL));
