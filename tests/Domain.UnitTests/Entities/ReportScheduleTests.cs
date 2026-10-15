using MyWealthV2.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class ReportScheduleTests
{
    [Test]
    public void Create_SetsTheDefinition_ReviseReplacesIt_DisableClearsActive()
    {
        var schedule = ReportSchedule.Create(
            tenantId: 4,
            reportId: 9,
            parametersJson: """{"customerId":"8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11"}""",
            isActive: true,
            cron: "0 9 1 * *");

        schedule.PublicId.ShouldNotBe(Guid.Empty);
        schedule.TenantId.ShouldBe(4);
        schedule.ReportId.ShouldBe(9);
        schedule.ParametersJson.ShouldBe("""{"customerId":"8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11"}""");
        schedule.IsActive.ShouldBeTrue();
        schedule.Cron.ShouldBe("0 9 1 * *");

        schedule.Revise(
            """{"customerId":"5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22"}""",
            isActive: false,
            cron: "15 8 * * 1");

        schedule.ParametersJson.ShouldBe("""{"customerId":"5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22"}""");
        schedule.IsActive.ShouldBeFalse();
        schedule.Cron.ShouldBe("15 8 * * 1");
        schedule.ReportId.ShouldBe(9);
        schedule.TenantId.ShouldBe(4);

        schedule.Disable();

        schedule.IsActive.ShouldBeFalse();
        schedule.Cron.ShouldBe("15 8 * * 1");
        schedule.ParametersJson.ShouldBe("""{"customerId":"5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22"}""");
    }
}
