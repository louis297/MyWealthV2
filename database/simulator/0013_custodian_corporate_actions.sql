-- In-kind transfers R68: whether the simulator custodian has applied an effective corporate action to its positions.
-- An action that was already effective before this script is taken as applied, so the custodian does not apply it again.
ALTER TABLE [MarketCorporateActions] ADD [CustodianApplied] bit NOT NULL CONSTRAINT [DF_MarketCorporateActions_CustodianApplied] DEFAULT (0);
EXEC(N'UPDATE [MarketCorporateActions] SET [CustodianApplied] = 1 WHERE [Status] = 1;');
