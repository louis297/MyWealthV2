-- Customer tax profiles (docs/features/interest-run.md §6.1).
-- One row per Customer: tax residency, whether the firm holds the IRD number, and the RWT rate the Customer chose.
-- The IRD number itself is never stored. A Customer without a row is "not provided" (R2). Forward only.

CREATE TABLE [CustomerTaxProfiles] (
    [UserId] int NOT NULL,
    [TenantId] int NOT NULL,
    [TaxResidency] int NOT NULL,
    [IrdNumberHeld] bit NOT NULL,
    [RwtRatePercent] decimal(5,2) NULL,
    [UpdatedByUserId] int NOT NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CustomerTaxProfiles] PRIMARY KEY ([UserId]),
    CONSTRAINT [FK_CustomerTaxProfiles_Users_TenantId_UserId]
        FOREIGN KEY ([TenantId], [UserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_CustomerTaxProfiles_Users_TenantId_UpdatedByUserId]
        FOREIGN KEY ([TenantId], [UpdatedByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [CK_CustomerTaxProfiles_TaxResidency] CHECK ([TaxResidency] BETWEEN 0 AND 1),
    -- R3: one of the five rates an individual may choose.
    CONSTRAINT [CK_CustomerTaxProfiles_RwtRatePercent] CHECK ([RwtRatePercent] IN (10.5, 17.5, 30, 33, 39)),
    -- R3: a rate only for a New Zealand Customer whose IRD number is held.
    CONSTRAINT [CK_CustomerTaxProfiles_RwtRateNeedsIrd] CHECK (
        [RwtRatePercent] IS NULL OR ([TaxResidency] = 0 AND [IrdNumberHeld] = 1))
);

CREATE INDEX [IX_CustomerTaxProfiles_TenantId] ON [CustomerTaxProfiles] ([TenantId]);
