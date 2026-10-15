namespace MyWealthV2.Application.Common.Interfaces;

public interface IReportFileStore
{
    Task SaveAsync(string relativeKey, byte[] pdf, CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(string relativeKey, CancellationToken cancellationToken);
}
