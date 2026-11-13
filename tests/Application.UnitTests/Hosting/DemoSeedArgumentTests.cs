using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Hosting;

// demo-tenant-seed R1: webapi runs the demo seed only when its first argument is exactly --seed-demo.
public class DemoSeedArgumentTests
{
    [Test]
    public void FirstArgument_IsDetected()
    {
        DemoTenantSeed.IsRequested(["--seed-demo"]).ShouldBeTrue();
        DemoTenantSeed.IsRequested(["--seed-demo", "--urls", "http://localhost:5000"]).ShouldBeTrue();
    }

    [TestCase]
    [TestCase("--healthcheck")]
    [TestCase("--urls", "--seed-demo")]
    [TestCase("--SEED-DEMO")]
    [TestCase("--seed-demo=true")]
    [TestCase("seed-demo")]
    public void AnythingElse_IsNotDetected(params string[] args)
    {
        DemoTenantSeed.IsRequested(args).ShouldBeFalse();
    }
}
