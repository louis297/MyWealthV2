CREATE TABLE [InstrumentPrices] (
    [InstrumentId] int NOT NULL,
    [Price] decimal(18, 8) NOT NULL,
    [AsOfDate] date NOT NULL,
    [Source] nvarchar(50) NOT NULL,
    [FetchedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_InstrumentPrices] PRIMARY KEY ([InstrumentId], [AsOfDate]),
    CONSTRAINT [FK_InstrumentPrices_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Id])
);

CREATE INDEX [IX_InstrumentPrices_FetchedAt] ON [InstrumentPrices] ([FetchedAt]);
