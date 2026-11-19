-- Transaction source (docs/features/transaction-source.md §6).
-- Every Transaction gets SourceKind (Manual = 0, Seed = 1, Rebalance = 2) and SourcePublicId (the rebalance
-- handle for Rebalance, otherwise NULL). Existing rows are back-filled: rebalance trades become Rebalance and lose
-- their internal-id reference, the exact seed references become Seed, every other row stays Manual.
-- The applicator runs this script without a surrounding transaction, so it first checks the rebalance rows
-- read-only and stops with one error before it changes anything.

-- A rebalance trade: 'rb:' + 32 hexadecimal digits + ':' + B or S + ':' + digits.
IF EXISTS (
    SELECT 1
    FROM [Transactions]
    WHERE [Reference] COLLATE Latin1_General_100_BIN2
              LIKE N'rb:' + REPLICATE(N'[0-9a-fA-F]', 32) + N':[BS]:[0-9]%'
      AND SUBSTRING([Reference], 39, 100) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'
      AND [Type] NOT IN (7, 8))
BEGIN
    THROW 50000, N'0035_transaction_source: a rebalance reference is on a Transaction that is not a Buy or a Sell.', 1;
END;

ALTER TABLE [Transactions] ADD
    [SourceKind] int NOT NULL CONSTRAINT [DF_Transactions_SourceKind] DEFAULT (0),
    [SourcePublicId] uniqueidentifier NULL;

-- The new columns are referenced inside EXEC, so this batch compiles before they exist.
EXEC(N'
UPDATE [Transactions]
SET [SourceKind] = 2,
    [SourcePublicId] = CAST(STUFF(STUFF(STUFF(STUFF(SUBSTRING([Reference], 4, 32), 9, 0, ''-''), 14, 0, ''-''), 19, 0, ''-''), 24, 0, ''-'') AS uniqueidentifier),
    [Reference] = NULL
WHERE [Reference] COLLATE Latin1_General_100_BIN2
          LIKE N''rb:'' + REPLICATE(N''[0-9a-fA-F]'', 32) + N'':[BS]:[0-9]%''
  AND SUBSTRING([Reference], 39, 100) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9]%''
  AND [Type] IN (7, 8);

UPDATE [Transactions]
SET [SourceKind] = 1
WHERE [Reference] COLLATE Latin1_General_100_BIN2 IN (
    N''TestSeed-DemoCustodial-Funding'',
    N''TestSeed-DemoCustodial-BuyVti'',
    N''TestSeed-DemoCustodial-BuyAapl'',
    N''TestSeed-DemoBrokerage-OpeningHoldings'',
    N''TestSeed-DemoBrokerage-Funding'',
    N''TestSeed-DemoBrokerage-Buy'',
    N''TestSeed-DemoBrokerage-StockIn'',
    N''TestSeed-DemoBrokerage-Split'',
    N''TestSeed-DemoEverydayBank-OpeningTransfer'',
    N''Smoke-Cash-TransferIn'');

ALTER TABLE [Transactions] DROP CONSTRAINT [DF_Transactions_SourceKind];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 2);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] = 2 AND [SourcePublicId] IS NOT NULL));
');
