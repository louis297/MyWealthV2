-- Corporate actions (docs/features/corporate-actions.md §6.1).
-- The corporate action announcements a market data connection's feed delivers (platform data, no TenantId); a
-- Tenant's corporate actions on its instruments, from the feed or entered by hand; one entitlement per Custodial
-- account that held the instrument when an action took effect; the corporate action detail of an entitlement
-- posting; source kind CorporateAction (7); the CorporateActions feed of a market data connection and the
-- InvalidItem failure. Forward only. No back-fill: no corporate action exists before this script, no Transaction
-- has source CorporateAction, and a hand Split on an External account keeps source Manual and gets no detail row.

CREATE TABLE [CorporateActionAnnouncements] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [Connection] nvarchar(50) NOT NULL,
    [OutsideId] nvarchar(100) NOT NULL,
    [PartyActionId] nvarchar(100) NOT NULL,
    [Version] int NOT NULL,
    [PartyStatus] int NOT NULL,
    [Symbol] nvarchar(32) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Kind] int NULL,
    [RatioNew] int NOT NULL,
    [RatioHeld] int NOT NULL,
    [ExDate] date NOT NULL,
    [RecordDate] date NULL,
    [PaymentDate] date NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    [Status] int NOT NULL,
    [SkipReason] int NULL,
    [ProcessedAt] datetimeoffset NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CorporateActionAnnouncements] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CorporateActionAnnouncements_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [CK_CorporateActionAnnouncements_Version] CHECK ([Version] >= 1),
    -- PartyActionStatus: Announced, Effective, Cancelled (R7).
    CONSTRAINT [CK_CorporateActionAnnouncements_PartyStatus] CHECK ([PartyStatus] BETWEEN 0 AND 2),
    -- CorporateActionKind: Split, Consolidation, Bonus; NULL for a kind the adapter did not know (R9).
    CONSTRAINT [CK_CorporateActionAnnouncements_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CorporateActionAnnouncements_Ratio] CHECK ([RatioNew] >= 1 AND [RatioHeld] >= 1),
    -- AnnouncementStatus: Received, Processed, Skipped (R10, R11).
    CONSTRAINT [CK_CorporateActionAnnouncements_Status] CHECK ([Status] BETWEEN 0 AND 2),
    -- AnnouncementSkipReason: UnsupportedKind, Invalid, NoInstrument, AlreadyEffective, Superseded; set iff Skipped.
    CONSTRAINT [CK_CorporateActionAnnouncements_SkipReason] CHECK ([SkipReason] BETWEEN 0 AND 4),
    CONSTRAINT [CK_CorporateActionAnnouncements_Skipped] CHECK (([Status] = 2 AND [SkipReason] IS NOT NULL) OR ([Status] <> 2 AND [SkipReason] IS NULL))
);

-- R9: an announcement is stored once per connection and outside id.
CREATE UNIQUE INDEX [UX_CorporateActionAnnouncements_Connection_OutsideId]
    ON [CorporateActionAnnouncements] ([Connection], [OutsideId]);
CREATE INDEX [IX_CorporateActionAnnouncements_Connection_Status_ReceivedAt]
    ON [CorporateActionAnnouncements] ([Connection], [Status], [ReceivedAt]);

CREATE TABLE [CorporateActions] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [TenantId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [Kind] int NOT NULL,
    [RatioNew] int NOT NULL,
    [RatioHeld] int NOT NULL,
    [ExDate] date NOT NULL,
    [RecordDate] date NULL,
    [PaymentDate] date NULL,
    [Origin] int NOT NULL,
    [Connection] nvarchar(50) NULL,
    [PartyActionId] nvarchar(100) NULL,
    [FeedVersion] int NULL,
    [Status] int NOT NULL,
    [Memo] nvarchar(200) NULL,
    [EffectiveAt] datetimeoffset NULL,
    [EffectiveByUserId] int NULL,
    [AppliedAt] datetimeoffset NULL,
    [CancelledAt] datetimeoffset NULL,
    [CancelledByUserId] int NULL,
    [CancelNote] nvarchar(200) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CorporateActions] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CorporateActions_PublicId] UNIQUE ([PublicId]),
    CONSTRAINT [UQ_CorporateActions_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_CorporateActions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_CorporateActions_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    CONSTRAINT [FK_CorporateActions_Users_TenantId_EffectiveByUserId]
        FOREIGN KEY ([TenantId], [EffectiveByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    CONSTRAINT [FK_CorporateActions_Users_TenantId_CancelledByUserId]
        FOREIGN KEY ([TenantId], [CancelledByUserId]) REFERENCES [Users] ([TenantId], [Id]),
    -- CorporateActionKind: Split, Consolidation, Bonus (R3).
    CONSTRAINT [CK_CorporateActions_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CorporateActions_Ratio] CHECK ([RatioNew] BETWEEN 1 AND 10000 AND [RatioHeld] BETWEEN 1 AND 10000),
    -- CorporateActionOrigin: Manual, Feed (R5). A Feed action keeps its connection, party action id and version.
    CONSTRAINT [CK_CorporateActions_Origin] CHECK ([Origin] BETWEEN 0 AND 1),
    CONSTRAINT [CK_CorporateActions_Feed] CHECK (
        ([Origin] = 1 AND [Connection] IS NOT NULL AND [PartyActionId] IS NOT NULL AND [FeedVersion] IS NOT NULL)
        OR ([Origin] = 0 AND [Connection] IS NULL AND [PartyActionId] IS NULL AND [FeedVersion] IS NULL)),
    -- CorporateActionStatus: Announced, Effective, Applied, Cancelled (R6).
    CONSTRAINT [CK_CorporateActions_Status] CHECK ([Status] BETWEEN 0 AND 3),
    CONSTRAINT [CK_CorporateActions_EffectiveAt] CHECK (([Status] IN (1, 2) AND [EffectiveAt] IS NOT NULL) OR ([Status] IN (0, 3) AND [EffectiveAt] IS NULL)),
    CONSTRAINT [CK_CorporateActions_AppliedAt] CHECK (([Status] = 2 AND [AppliedAt] IS NOT NULL) OR ([Status] <> 2 AND [AppliedAt] IS NULL)),
    CONSTRAINT [CK_CorporateActions_CancelledAt] CHECK (([Status] = 3 AND [CancelledAt] IS NOT NULL) OR ([Status] <> 3 AND [CancelledAt] IS NULL))
);

-- R5, R17: a Feed action is unique per instrument, connection and party action id; a Manual one that is not
-- Cancelled per instrument, kind and ex-date.
CREATE UNIQUE INDEX [UX_CorporateActions_InstrumentId_Connection_PartyActionId]
    ON [CorporateActions] ([InstrumentId], [Connection], [PartyActionId]) WHERE [Origin] = 1;
CREATE UNIQUE INDEX [UX_CorporateActions_InstrumentId_Kind_ExDate]
    ON [CorporateActions] ([InstrumentId], [Kind], [ExDate]) WHERE [Origin] = 0 AND [Status] <> 3;
CREATE INDEX [IX_CorporateActions_TenantId_ExDate] ON [CorporateActions] ([TenantId], [ExDate] DESC);
CREATE INDEX [IX_CorporateActions_Status_EffectiveAt] ON [CorporateActions] ([Status], [EffectiveAt]);

CREATE TABLE [CorporateActionEntitlements] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [CorporateActionId] int NOT NULL,
    [AccountId] int NOT NULL,
    [Status] int NOT NULL,
    [Reason] int NULL,
    [QuantityBefore] decimal(18, 8) NULL,
    [QuantityAfter] decimal(18, 8) NULL,
    [TransactionId] int NULL,
    [PostedAt] datetimeoffset NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CorporateActionEntitlements] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CorporateActionEntitlements_CorporateActionId_AccountId] UNIQUE ([CorporateActionId], [AccountId]),
    CONSTRAINT [FK_CorporateActionEntitlements_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_CorporateActionEntitlements_CorporateActions_TenantId_CorporateActionId]
        FOREIGN KEY ([TenantId], [CorporateActionId]) REFERENCES [CorporateActions] ([TenantId], [Id]),
    CONSTRAINT [FK_CorporateActionEntitlements_Accounts_TenantId_AccountId]
        FOREIGN KEY ([TenantId], [AccountId]) REFERENCES [Accounts] ([TenantId], [Id]),
    CONSTRAINT [FK_CorporateActionEntitlements_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    -- EntitlementStatus: Pending, Posted, NotApplied (R25).
    CONSTRAINT [CK_CorporateActionEntitlements_Status] CHECK ([Status] BETWEEN 0 AND 2),
    -- EntitlementReason: NoHolding, TooSmall; set iff NotApplied.
    CONSTRAINT [CK_CorporateActionEntitlements_Reason] CHECK ([Reason] BETWEEN 0 AND 1),
    CONSTRAINT [CK_CorporateActionEntitlements_NotApplied] CHECK (([Status] = 2 AND [Reason] IS NOT NULL) OR ([Status] <> 2 AND [Reason] IS NULL)),
    -- Posted iff it has both quantities and the Transaction.
    CONSTRAINT [CK_CorporateActionEntitlements_Posted] CHECK (
        ([Status] = 1 AND [TransactionId] IS NOT NULL AND [QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL)
        OR ([Status] <> 1 AND [TransactionId] IS NULL AND [QuantityBefore] IS NULL AND [QuantityAfter] IS NULL)),
    CONSTRAINT [CK_CorporateActionEntitlements_PostedAt] CHECK (([Status] = 0 AND [PostedAt] IS NULL) OR ([Status] <> 0 AND [PostedAt] IS NOT NULL))
);

CREATE INDEX [IX_CorporateActionEntitlements_Pending]
    ON [CorporateActionEntitlements] ([CorporateActionId], [AccountId]) WHERE [Status] = 0;
CREATE INDEX [IX_CorporateActionEntitlements_TenantId] ON [CorporateActionEntitlements] ([TenantId]);

-- R27: the corporate action detail of an entitlement posting and of its Reversal.
CREATE TABLE [TransactionCorporateActionDetails] (
    [TransactionId] int NOT NULL,
    [TenantId] int NOT NULL,
    [Kind] int NOT NULL,
    [RatioNew] int NOT NULL,
    [RatioHeld] int NOT NULL,
    [ExDate] date NOT NULL,
    [RecordDate] date NULL,
    [PaymentDate] date NULL,
    [QuantityBefore] decimal(18, 8) NOT NULL,
    [QuantityAfter] decimal(18, 8) NOT NULL,
    CONSTRAINT [PK_TransactionCorporateActionDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionCorporateActionDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_TransactionCorporateActionDetails_Transactions_TenantId_TransactionId]
        FOREIGN KEY ([TenantId], [TransactionId]) REFERENCES [Transactions] ([TenantId], [Id]),
    CONSTRAINT [CK_TransactionCorporateActionDetails_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
    CONSTRAINT [CK_TransactionCorporateActionDetails_Ratio] CHECK ([RatioNew] >= 1 AND [RatioHeld] >= 1),
    CONSTRAINT [CK_TransactionCorporateActionDetails_Quantities] CHECK ([QuantityBefore] > 0 AND [QuantityAfter] > 0)
);

CREATE INDEX [IX_TransactionCorporateActionDetails_TenantId] ON [TransactionCorporateActionDetails] ([TenantId]);

-- Transaction source kind CorporateAction = 7, with the corporate action's PublicId as source id.
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourceKind];
ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_SourcePublicId];
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourceKind] CHECK ([SourceKind] BETWEEN 0 AND 7);
ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_SourcePublicId] CHECK (
    ([SourceKind] IN (0, 1) AND [SourcePublicId] IS NULL)
    OR ([SourceKind] IN (2, 3, 4, 5, 6, 7) AND [SourcePublicId] IS NOT NULL));

-- MarketDataFeedKind: Prices, CorporateActions (with a cursor). MarketDataFailure gains InvalidItem (5).
ALTER TABLE [MarketDataFeeds] DROP CONSTRAINT [CK_MarketDataFeeds_Kind];
ALTER TABLE [MarketDataFeeds] DROP CONSTRAINT [CK_MarketDataFeeds_LastFailure];
ALTER TABLE [MarketDataFeeds] ADD CONSTRAINT [CK_MarketDataFeeds_Kind] CHECK ([Kind] BETWEEN 0 AND 1);
ALTER TABLE [MarketDataFeeds] ADD CONSTRAINT [CK_MarketDataFeeds_LastFailure] CHECK ([LastFailure] BETWEEN 0 AND 5);
