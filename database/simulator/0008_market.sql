-- The simulator market, data vendor half (docs/features/simulated-instruments.md §6.2, R14–R24).
-- The simulator's own made-up instruments (symbols ending in .SIM) and one price per simulator day for each. The
-- instrument row holds the price of its last priced day; MarketPrices holds every day, IsSet marking a price the
-- operator typed. A reset empties both tables and adds the standing instruments again (R23).

CREATE TABLE [MarketInstruments] (
    [Id] int NOT NULL IDENTITY,
    [Symbol] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Currency] char(3) NOT NULL,
    [DailyMovePercent] decimal(5, 2) NOT NULL,
    [Price] decimal(18, 4) NOT NULL,
    [PriceDate] date NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MarketInstruments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MarketInstruments_DailyMovePercent] CHECK ([DailyMovePercent] BETWEEN 0 AND 20),
    CONSTRAINT [CK_MarketInstruments_Price] CHECK ([Price] > 0)
);

-- The database collation compares the symbol without regard to case (R15).
CREATE UNIQUE INDEX [UX_MarketInstruments_Symbol] ON [MarketInstruments] ([Symbol]);

CREATE TABLE [MarketPrices] (
    [InstrumentId] int NOT NULL,
    [PriceDate] date NOT NULL,
    [Price] decimal(18, 4) NOT NULL,
    [IsSet] bit NOT NULL,
    CONSTRAINT [PK_MarketPrices] PRIMARY KEY ([InstrumentId], [PriceDate]),
    CONSTRAINT [FK_MarketPrices_MarketInstruments_InstrumentId]
        FOREIGN KEY ([InstrumentId]) REFERENCES [MarketInstruments] ([Id]),
    CONSTRAINT [CK_MarketPrices_Price] CHECK ([Price] > 0)
);
