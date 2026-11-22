-- Market data feeds (docs/features/simulated-instruments.md §6.1).
-- The state of each feed of each market data connection: when it was last read and how that went (ADR 0021
-- rule 16). Platform data: no TenantId, like InstrumentPrices. A connection lives in configuration, so there is no
-- FK. The Prices feed (Kind 0) has no cursor. InstrumentPrices does not change; its Source column holds the
-- adapter name (Simulator) on rows a connection writes. Forward only; no existing row changes.

CREATE TABLE [MarketDataFeeds] (
    [Connection] nvarchar(50) NOT NULL,
    [Kind] int NOT NULL,
    [Cursor] nvarchar(500) NULL,
    [LastSucceededAt] datetimeoffset NULL,
    [LastFailedAt] datetimeoffset NULL,
    [LastFailure] int NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_MarketDataFeeds] PRIMARY KEY ([Connection], [Kind]),
    -- MarketDataFeedKind: Prices.
    CONSTRAINT [CK_MarketDataFeeds_Kind] CHECK ([Kind] = 0),
    -- MarketDataFailure: Unreachable, Timeout, Unauthorized, Unavailable, BadResponse (R5).
    CONSTRAINT [CK_MarketDataFeeds_LastFailure] CHECK ([LastFailure] BETWEEN 0 AND 4)
);
