using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace MyWealthV2.Smoke.Tests;

/// <summary>S1 (docs/smoke-test.md §2).</summary>
public class AdviserPortalSmokeTests : SmokeTest
{
    [Test]
    public async Task SmokeAdviser_SignsIn_AndSeesTheDashboardNetWorth()
    {
        var password = SmokeSettings.AdviserPassword;

        await Page.GotoAsync(SmokeSettings.AdviserPortalUrl.ToString());
        await SignInAsync(SmokeSettings.AdviserEmail, password);

        await Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        // Rendered only inside NetWorthView, after the net-worth read returns.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "By customer" })).ToBeVisibleAsync();
    }
}
