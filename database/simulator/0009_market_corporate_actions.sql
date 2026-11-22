-- The simulator market, issuer half (docs/features/corporate-actions.md §6.2, R39–R47).
-- The corporate actions of the simulator's own instruments, and every version of each: an announce, an amend, a
-- cancel and the effective step each append one version, in the order they happen. A version's Id is its place in the
-- feed. A reset empties both tables (R47).

CREATE TABLE [MarketCorporateActions] (
    [Id] int NOT NULL IDENTITY,
    [Number] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Kind] int NOT NULL,
    [RatioNew] int NOT NULL,
    [RatioHeld] int NOT NULL,
    [ExDate] date NOT NULL,
    [RecordDate] date NULL,
    [PaymentDate] date NULL,
    [Status] int NOT NULL,
    [Version] int NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MarketCorporateActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MarketCorporateActions_MarketInstruments_InstrumentId]
        FOREIGN KEY ([InstrumentId]) REFERENCES [MarketInstruments] ([Id]),
    -- Split, Consolidation, Bonus.
    CONSTRAINT [CK_MarketCorporateActions_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
    CONSTRAINT [CK_MarketCorporateActions_Ratio] CHECK ([RatioNew] BETWEEN 1 AND 10000 AND [RatioHeld] BETWEEN 1 AND 10000),
    -- Announced, Effective, Cancelled.
    CONSTRAINT [CK_MarketCorporateActions_Status] CHECK ([Status] BETWEEN 0 AND 2),
    CONSTRAINT [CK_MarketCorporateActions_Version] CHECK ([Version] >= 1)
);

-- R43: n counts the actions of the world from 1.
CREATE UNIQUE INDEX [UX_MarketCorporateActions_Number] ON [MarketCorporateActions] ([Number]);

CREATE TABLE [MarketCorporateActionVersions] (
    [Id] bigint NOT NULL IDENTITY,
    [ActionId] int NOT NULL,
    [Version] int NOT NULL,
    [Status] int NOT NULL,
    [RatioNew] int NOT NULL,
    [RatioHeld] int NOT NULL,
    [ExDate] date NOT NULL,
    [RecordDate] date NULL,
    [PaymentDate] date NULL,
    CONSTRAINT [PK_MarketCorporateActionVersions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MarketCorporateActionVersions_MarketCorporateActions_ActionId]
        FOREIGN KEY ([ActionId]) REFERENCES [MarketCorporateActions] ([Id]),
    CONSTRAINT [CK_MarketCorporateActionVersions_Status] CHECK ([Status] BETWEEN 0 AND 2)
);

CREATE UNIQUE INDEX [UX_MarketCorporateActionVersions_ActionId_Version] ON [MarketCorporateActionVersions] ([ActionId], [Version]);
