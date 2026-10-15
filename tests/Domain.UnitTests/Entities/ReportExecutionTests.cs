using MyWealthV2.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class ReportExecutionTests
{
    [Test]
    public void Succeed_SetsTheKey_AndFail_ClearsIt()
    {
        var execution = ReportExecution.Queue(1, 2, "Manual", "{}", 3);
        var key = "11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222.pdf";

        execution.Fail("disk full");
        execution.Status.ShouldBe("Failed");
        execution.ResultStorageKey.ShouldBeNull();
        execution.ErrorMessage.ShouldBe("disk full");

        execution.Succeed(key);
        execution.Status.ShouldBe("Succeeded");
        execution.ResultStorageKey.ShouldBe(key);
        execution.ErrorMessage.ShouldBeNull();

        execution.Fail(new string('x', 2001));
        execution.Status.ShouldBe("Failed");
        execution.ResultStorageKey.ShouldBeNull();
        execution.ErrorMessage.ShouldNotBeNull();
        execution.ErrorMessage!.Length.ShouldBe(2000);
    }
}
