-- External caller numbers (docs/features/external-caller-numbers.md §6).
-- A Brokerage fee detail may have no schedule: on an External account the caller enters the fee.
-- Every existing row has a schedule; no data changes. No CHECK replaces this one, because which fees
-- may lack a schedule depends on the Account, which this table does not join.

ALTER TABLE [TransactionFeeDetails] DROP CONSTRAINT [CK_TransactionFeeDetails_BrokerageSchedule];
