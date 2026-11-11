using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;

namespace MyWealthV2.Smoke.Tests;

/// <summary>
/// One fresh browser context per test. Keeps a trace and a screenshot when a test fails.
/// </summary>
public abstract class SmokeTest : PageTest
{
    private static readonly float NavigationTimeout = (float)TimeSpan.FromSeconds(30).TotalMilliseconds;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        // The local Development certificate may not be trusted by the Playwright browser.
        IgnoreHTTPSErrors = SmokeSettings.TargetsLocalhost
    };

    [SetUp]
    public async Task StartTracing()
    {
        Page.SetDefaultNavigationTimeout(NavigationTimeout);
        Page.SetDefaultTimeout(NavigationTimeout);
        await Context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
    }

    [TearDown]
    public async Task StopTracing()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        if (!failed)
        {
            await Context.Tracing.StopAsync();
            return;
        }

        var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "playwright");
        Directory.CreateDirectory(folder);
        var name = TestContext.CurrentContext.Test.Name;
        var screenshot = Path.Combine(folder, $"{name}.png");
        var trace = Path.Combine(folder, $"{name}.trace.zip");
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true });
        await Context.Tracing.StopAsync(new TracingStopOptions { Path = trace });
        TestContext.AddTestAttachment(screenshot);
        TestContext.AddTestAttachment(trace);
    }

    /// <summary>Fills the hosted login form on identity (src/IdentityHost/Pages/Login.cshtml).</summary>
    protected async Task SignInAsync(string email, string password)
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Password").FillAsync(password);
        await Page.GetByLabel("Tenant code").FillAsync(SmokeSettings.TenantCode);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
