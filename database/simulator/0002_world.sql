-- Simulator world and control log, docs/features/simulator.md §6.2 and R20–R29.
-- The one World row is created by the simulator on its first start (R20), not here, so its WorldId and seed
-- are random.

CREATE TABLE [World] (
    [Id] int NOT NULL,
    [WorldId] uniqueidentifier NOT NULL,
    [Seed] int NOT NULL,
    [ClockOffsetSeconds] bigint NOT NULL,
    [PausedAt] datetimeoffset NULL,
    [PartyOutage] bit NOT NULL,
    [StartedAt] datetimeoffset NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_World] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_World_SingleRow] CHECK ([Id] = 1),
    CONSTRAINT [CK_World_ClockOffsetSeconds] CHECK ([ClockOffsetSeconds] >= 0)
);

CREATE TABLE [ControlLog] (
    [Id] bigint NOT NULL IDENTITY,
    [At] datetimeoffset NOT NULL,
    [SimulatorAt] datetimeoffset NOT NULL,
    [Actor] nvarchar(256) NOT NULL,
    [Action] nvarchar(50) NOT NULL,
    [Detail] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_ControlLog] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_ControlLog_At] ON [ControlLog] ([At] DESC);
