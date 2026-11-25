using Microsoft.Extensions.Configuration;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests;

/// <summary>A test host builder holds no file watcher, so a test that never builds it leaks no inotify instance.</summary>
public class TestHostTests
{
    [Test]
    public void CreateBuilder_KeepsTheDefaultJsonSources_ButNoneReloadsOnChange()
    {
        var builder = TestHost.CreateBuilder();

        var files = builder.Configuration.Sources.OfType<FileConfigurationSource>().ToList();
        files.ShouldNotBeEmpty();
        files.ShouldAllBe(source => !source.ReloadOnChange);
    }
}
