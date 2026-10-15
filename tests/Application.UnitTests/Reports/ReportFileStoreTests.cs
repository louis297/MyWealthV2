using Microsoft.Extensions.Options;
using MyWealthV2.Application.Reports;
using MyWealthV2.Infrastructure.Reports;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

public class ReportFileStoreTests
{
    [Test]
    public async Task Save_WritesTheRelativeKey_AndDeleteRemovesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "mywealth-reports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ReportFileStore(Options.Create(new ReportStorageOptions { StorageRoot = root }));
            var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var execution = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var key = $"{tenant:D}/{execution:D}.pdf";
            var pdf = "%PDF-1.7 test"u8.ToArray();

            await store.SaveAsync(key, pdf, CancellationToken.None);

            var path = Path.Combine(root, tenant.ToString("D"), execution.ToString("D") + ".pdf");
            File.Exists(path).ShouldBeTrue();
            (await File.ReadAllBytesAsync(path)).ShouldBe(pdf);

            await store.DeleteIfExistsAsync(key, CancellationToken.None);
            File.Exists(path).ShouldBeFalse();

            await Should.ThrowAsync<ArgumentException>(
                () => store.SaveAsync("../secret.pdf", pdf, CancellationToken.None));
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
