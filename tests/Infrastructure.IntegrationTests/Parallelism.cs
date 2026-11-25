using MyWealthV2.Infrastructure.IntegrationTests;
using MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

// Fixtures marked [Parallelizable] run at the same time as each other. Each fixture in the AppliedSchema
// namespace leases its own applied schema database from SchemaDatabasePool, so fixtures that run at the
// same time never share one. NUnit never runs them at the same time as fixtures without the attribute.
//
// Mark a new AppliedSchema fixture [Parallelizable] unless it changes state outside its database:
// process-wide settings, environment variables or fixed paths on disk. Fixtures that call AddHangfire
// stay serial: it sets the process-wide JobStorage.Current, so two of them at once see each other's jobs.
[assembly: LevelOfParallelism(12)]
[assembly: ReturnSchemaDatabase]

namespace MyWealthV2.Infrastructure.IntegrationTests;

/// <summary>Gives a fixture's applied schema database back to the pool when the fixture ends.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ReturnSchemaDatabaseAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Suite;

    public void BeforeTest(ITest test)
    {
    }

    public void AfterTest(ITest test)
    {
        if (test is TestFixture fixture)
        {
            SchemaDatabaseSetup.ReturnDatabase(fixture);
        }
    }
}
