-- Simulator database (MyWealthSimulatorDb), docs/features/simulator.md §6.2.
-- The same shape as the platform's SchemaVersions. Never applied to MyWealthDbV2.

CREATE TABLE [SchemaVersions] (
    [Id] int NOT NULL IDENTITY,
    [ScriptName] nvarchar(200) NOT NULL,
    [AppliedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_SchemaVersions] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_SchemaVersions_ScriptName] UNIQUE ([ScriptName])
);
