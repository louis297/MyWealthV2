CREATE TABLE [SchemaVersions] (
    [Id] int NOT NULL IDENTITY,
    [ScriptName] nvarchar(200) NOT NULL,
    [AppliedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_SchemaVersions] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_SchemaVersions_ScriptName] UNIQUE ([ScriptName])
);
