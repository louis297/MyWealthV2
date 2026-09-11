using MyWealthV2.Infrastructure.Data.Schema;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Schema;

public class SchemaApplicatorTests
{
    [Test]
    public async Task AppliesUnappliedScriptsInOrder()
    {
        var source = new FakeScriptSource(
            new SchemaScript("0001_schema_versions.sql", "sql-1"),
            new SchemaScript("0003_identity.sql", "sql-3"));
        var versions = new FakeVersionStore();
        var executor = new FakeExecutor();
        var applicator = new SchemaApplicator(source, versions, executor);

        await applicator.ApplyAsync(CancellationToken.None);

        executor.Executed.ShouldBe(["sql-1", "sql-3"]);
        versions.Applied.ShouldBe(["0001_schema_versions.sql", "0003_identity.sql"]);
    }

    [Test]
    public async Task SkipsScriptsAlreadyRecorded()
    {
        var source = new FakeScriptSource(
            new SchemaScript("0001_schema_versions.sql", "sql-1"),
            new SchemaScript("0003_identity.sql", "sql-3"));
        var versions = new FakeVersionStore("0001_schema_versions.sql");
        var executor = new FakeExecutor();
        var applicator = new SchemaApplicator(source, versions, executor);

        await applicator.ApplyAsync(CancellationToken.None);

        executor.Executed.ShouldBe(["sql-3"]);
        versions.Applied.ShouldBe(["0001_schema_versions.sql", "0003_identity.sql"]);
    }

    private sealed class FakeScriptSource(params SchemaScript[] scripts) : ISchemaScriptSource
    {
        public IReadOnlyList<SchemaScript> GetScripts() => scripts;
    }

    private sealed class FakeVersionStore : ISchemaVersionStore
    {
        public FakeVersionStore(params string[] applied)
        {
            Applied = [..applied];
        }

        public List<string> Applied { get; }

        public Task<IReadOnlySet<string>> GetAppliedNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Applied.ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task MarkAppliedAsync(string scriptName, CancellationToken cancellationToken)
        {
            Applied.Add(scriptName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExecutor : ISqlBatchExecutor
    {
        public List<string> Executed { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken)
        {
            Executed.Add(sql);
            return Task.CompletedTask;
        }
    }
}
