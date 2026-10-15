namespace MyWealthV2.Application.Reports;

public sealed class ReportStorageOptions
{
    public const string SectionName = "Reports";

    public string StorageRoot { get; set; } = string.Empty;
}
