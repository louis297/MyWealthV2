-- The simulator bank, returns and recalls (docs/features/bank-returns.md §6.2, R12–R15).
-- A payment either pays a payee or sends back a credit line of the account to its payer. ReturnOf is the outside id
-- as the platform sent it; ReturnOfLineId is that line when the account has it (a return of a line it does not have
-- is rejected as UnknownLine, and still recorded for the idempotency of R51). A recall is a BankLines row with
-- ReturnOfLineId, which 0004 already allows.

ALTER TABLE [BankPayments] ALTER COLUMN [PayeeAccountNumber] nvarchar(34) NULL;
ALTER TABLE [BankPayments] ALTER COLUMN [PayeeName] nvarchar(140) NULL;
ALTER TABLE [BankPayments] ADD
    [ReturnOf] nvarchar(100) NULL,
    [ReturnOfLineId] bigint NULL;

-- The new columns are referenced inside EXEC, so this batch compiles before they exist.
EXEC(N'
ALTER TABLE [BankPayments] ADD CONSTRAINT [FK_BankPayments_BankLines_ReturnOfLineId]
    FOREIGN KEY ([ReturnOfLineId]) REFERENCES [BankLines] ([Id]);
ALTER TABLE [BankPayments] ADD CONSTRAINT [CK_BankPayments_Target] CHECK (
    ([PayeeAccountNumber] IS NOT NULL AND [PayeeName] IS NOT NULL AND [ReturnOf] IS NULL AND [ReturnOfLineId] IS NULL)
    OR ([PayeeAccountNumber] IS NULL AND [PayeeName] IS NULL AND [ReturnOf] IS NOT NULL));
CREATE INDEX [IX_BankPayments_ReturnOfLineId] ON [BankPayments] ([ReturnOfLineId]) WHERE [ReturnOfLineId] IS NOT NULL;
');
