ALTER TABLE [Transactions] ADD [ReversedByTransactionId] int NULL;

EXEC(N'
ALTER TABLE [Transactions] ADD CONSTRAINT [FK_Transactions_Transactions_ReversedByTransactionId]
    FOREIGN KEY ([ReversedByTransactionId]) REFERENCES [Transactions] ([Id]);

CREATE UNIQUE INDEX [IX_Transactions_ReversedByTransactionId]
    ON [Transactions] ([ReversedByTransactionId])
    WHERE [ReversedByTransactionId] IS NOT NULL;
');
