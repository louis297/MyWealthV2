using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;
using static MyWealthV2.Application.FunctionalTests.MigrationBatches.MigrationBatchHttp;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Migration batches R32: the bank reconciliation of the trust account that received a batch's funding.</summary>
[Parallelizable]
public class MigrationBatchReconciliationTests : TestBase
{
    [Test]
    public async Task TheFundingOfABatchNotPosted_IsMigrationNotPosted_AndTheRunAgrees()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        await FundAsync(world, id, 250000m);

        var run = await RunAndGetAsync(world.TenantAdmin, world.Bank.ClientMoney.PublicId);

        Items(run).ShouldBe([("MigrationNotPosted", "Timing", 250000m, 1)]);
        Status(run).ShouldBe("Agreed");
        Decimal(run, "difference").ShouldBe(250000m);
    }

    [Test]
    public async Task AfterThePost_TheBatchLeavesNoItem_AndTheRunAgrees()
    {
        var world = await MigrationWorld.CreateAsync();
        await PostedAsync(world);

        var run = await RunAndGetAsync(world.TenantAdmin, world.Bank.ClientMoney.PublicId);

        Items(run).ShouldBeEmpty();
        Status(run).ShouldBe("Agreed");
        Decimal(run, "difference").ShouldBe(0m);
    }
}
