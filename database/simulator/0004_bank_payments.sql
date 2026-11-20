-- The simulator bank, payments side (docs/features/withdrawals.md §6.3, R48–R57).
-- Payments the platform asks the bank to make from a trust account, idempotent on the platform's instruction id
-- (R51), the payment mode the operator sets (R52), and the line columns a payment and its return need: the
-- instruction id the bank echoes, and the line a return takes back (R53, R54). A reset empties BankPayments and sets
-- the mode back to Execute (R56).

ALTER TABLE [World] ADD
    [NextPaymentNumber] bigint NOT NULL CONSTRAINT [DF_World_NextPaymentNumber] DEFAULT (1),
    [BankPaymentMode] int NOT NULL CONSTRAINT [DF_World_BankPaymentMode] DEFAULT (0),
    [BankPaymentRejectReason] nvarchar(30) NULL;

ALTER TABLE [BankLines] ADD
    [EndToEndId] nvarchar(100) NULL,
    [ReturnOfLineId] bigint NULL,
    [ReturnReason] nvarchar(140) NULL;

-- The new column is referenced inside EXEC, so this batch compiles before it exists.
EXEC(N'ALTER TABLE [BankLines] ADD CONSTRAINT [FK_BankLines_BankLines_ReturnOfLineId]
    FOREIGN KEY ([ReturnOfLineId]) REFERENCES [BankLines] ([Id]);');

CREATE TABLE [BankPayments] (
    [Id] bigint NOT NULL IDENTITY,
    [PaymentId] nvarchar(100) NOT NULL,
    [BankAccountId] int NOT NULL,
    [InstructionId] nvarchar(100) NOT NULL,
    [RequestHash] varbinary(32) NOT NULL,
    [Amount] decimal(18, 4) NOT NULL,
    [Reference] nvarchar(18) NULL,
    [PayeeAccountNumber] nvarchar(34) NOT NULL,
    [PayeeName] nvarchar(140) NOT NULL,
    [Status] int NOT NULL,
    [RejectReason] nvarchar(30) NULL,
    [Mode] int NOT NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    [DebitLineId] bigint NULL,
    [ReturnLineId] bigint NULL,
    CONSTRAINT [PK_BankPayments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BankPayments_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [BankAccounts] ([Id]),
    CONSTRAINT [FK_BankPayments_BankLines_DebitLineId] FOREIGN KEY ([DebitLineId]) REFERENCES [BankLines] ([Id]),
    CONSTRAINT [FK_BankPayments_BankLines_ReturnLineId] FOREIGN KEY ([ReturnLineId]) REFERENCES [BankLines] ([Id]),
    CONSTRAINT [CK_BankPayments_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_BankPayments_Status] CHECK ([Status] BETWEEN 0 AND 3),
    CONSTRAINT [CK_BankPayments_Mode] CHECK ([Mode] BETWEEN 0 AND 2)
);

CREATE UNIQUE INDEX [UX_BankPayments_PaymentId] ON [BankPayments] ([PaymentId]);

-- R51: one payment per instruction id on an account.
CREATE UNIQUE INDEX [UX_BankPayments_BankAccountId_InstructionId] ON [BankPayments] ([BankAccountId], [InstructionId]);
