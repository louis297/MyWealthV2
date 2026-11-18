-- Tenant custody modes (docs/features/tenant-custody-modes.md §6, ADR 0020 item 1).
-- Every Tenant gets its allowed custody modes as two flags. Existing rows allow both from the defaults.
-- The defaults stay: allowing both is the setting every Tenant had before this script.

ALTER TABLE [Tenants] ADD [AllowsExternalAccounts] bit NOT NULL
    CONSTRAINT [DF_Tenants_AllowsExternalAccounts] DEFAULT (1);

ALTER TABLE [Tenants] ADD [AllowsCustodialAccounts] bit NOT NULL
    CONSTRAINT [DF_Tenants_AllowsCustodialAccounts] DEFAULT (1);

EXEC(N'
ALTER TABLE [Tenants] ADD CONSTRAINT [CK_Tenants_AllowedCustodyModes]
    CHECK ([AllowsExternalAccounts] = 1 OR [AllowsCustodialAccounts] = 1);
');
