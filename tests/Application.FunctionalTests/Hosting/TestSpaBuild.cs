namespace MyWealthV2.Application.FunctionalTests.Hosting;

// A stand-in for a portal's Vite build: a webroot folder with index.html and one hashed asset.
internal sealed class TestSpaBuild : IDisposable
{
    public const string IndexBody = "<!doctype html><html><body>test-spa-index</body></html>";
    public const string AssetPath = "/assets/app.js";
    public const string AssetBody = "console.log('test-spa-asset');";

    private TestSpaBuild(string folder) => Folder = folder;

    public string Folder { get; }

    public static TestSpaBuild Create(bool withIndex = true)
    {
        var folder = TestFolders.Create("wwwroot");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        File.WriteAllText(Path.Combine(folder, "assets", "app.js"), AssetBody);
        if (withIndex)
        {
            File.WriteAllText(Path.Combine(folder, "index.html"), IndexBody);
        }

        return new TestSpaBuild(folder);
    }

    public void Dispose() => TestFolders.Delete(Folder);
}
