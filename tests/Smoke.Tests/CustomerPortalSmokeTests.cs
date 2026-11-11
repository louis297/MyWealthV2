using Microsoft.Playwright;

namespace MyWealthV2.Smoke.Tests;

/// <summary>S2 (docs/smoke-test.md §2).</summary>
public class CustomerPortalSmokeTests : SmokeTest
{
    [Test]
    public async Task SmokeCustomer_SignsIn_AndSeesTheirAccount()
    {
        var password = SmokeSettings.CustomerPassword;

        await Page.GotoAsync(new Uri(SmokeSettings.CustomerPortalUrl, "/accounts").ToString());
        await SignInAsync(SmokeSettings.CustomerEmail, password);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Accounts" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = SmokeSettings.AccountName })).ToBeVisibleAsync();
    }
}
