-- Simulator spec §6.1 (docs/features/simulator.md): fifth role SimulatorOperator (Role = 4).
-- Platform-level like SystemAdmin: no tenant, no adviser. No data change.
-- The applicator runs a script without a surrounding transaction, so the drop and the re-add share one here.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE [Users] DROP CONSTRAINT [CK_Users_RoleShape];

ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_RoleShape] CHECK (
    ([Role] IN (0, 4) AND [TenantId] IS NULL AND [AdviserId] IS NULL)
    OR ([Role] IN (1, 2) AND [TenantId] IS NOT NULL AND [AdviserId] IS NULL)
    OR ([Role] = 3 AND [TenantId] IS NOT NULL AND [AdviserId] IS NOT NULL));

COMMIT TRANSACTION;
