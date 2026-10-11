ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_Type];

ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_Type]
    CHECK ([Type] BETWEEN 0 AND 9);
