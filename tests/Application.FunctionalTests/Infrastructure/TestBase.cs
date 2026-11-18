using NUnit.Framework.Internal;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public abstract class TestBase
{
    // Set only while a [Parallelizable] fixture runs. Other fixtures use the default environment.
    internal TestEnvironment? Environment { get; private set; }

    [OneTimeSetUp]
    public async Task LeaseEnvironment()
    {
        var scope = TestContext.CurrentContext.Test.Properties.Get(PropertyNames.ParallelScope) as ParallelScope?;
        if (scope is null || (scope.Value & ParallelScope.Self) == 0)
        {
            return;
        }

        if ((scope.Value & ParallelScope.Children) != 0)
        {
            throw new InvalidOperationException(
                "Tests in one fixture share a database and reset it in SetUp, so they cannot run in parallel. " +
                "Use [Parallelizable(ParallelScope.Self)].");
        }

        Environment = await FunctionalTestSetup.LeaseEnvironmentAsync();
    }

    [OneTimeTearDown]
    public void ReturnEnvironment()
    {
        if (Environment is null)
        {
            return;
        }

        FunctionalTestSetup.ReturnEnvironment(Environment);
        Environment = null;
    }

    [SetUp]
    public async Task SetUp()
    {
        await TestApp.ResetState();
        await FunctionalTestSetup.ReseedAsync();
    }

    // The fixture that owns the calling test, or null outside a TestBase fixture.
    internal static TestBase? CurrentFixture => TestExecutionContext.CurrentContext.TestObject as TestBase;
}
