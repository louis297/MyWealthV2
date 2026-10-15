namespace MyWealthV2.Application.FunctionalTests.Reports;

public static class ReportStoragePaths
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "mywealth-reports-functional");
}
