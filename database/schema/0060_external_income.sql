-- Income on External accounts (docs/features/external-income.md §6).
-- A hand-entered dividend may have no amount per share and no ex-date (R17). A corporate action's dividend still
-- has both: Transaction.PostDividend holds that, because this table does not join the source. Forward only; every
-- existing row keeps its values, no back-fill. The CHECK on AmountPerShare is dropped first because a column a CHECK
-- reads cannot be altered.

ALTER TABLE [TransactionDividendDetails] DROP CONSTRAINT [CK_TransactionDividendDetails_AmountPerShare];
ALTER TABLE [TransactionDividendDetails] ALTER COLUMN [AmountPerShare] decimal(18, 8) NULL;
ALTER TABLE [TransactionDividendDetails] ALTER COLUMN [ExDate] date NULL;
ALTER TABLE [TransactionDividendDetails] ADD CONSTRAINT [CK_TransactionDividendDetails_AmountPerShare]
    CHECK ([AmountPerShare] IS NULL OR [AmountPerShare] > 0);
