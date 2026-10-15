using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Reports;

public interface IReportGenerator
{
    string Code { get; }

    Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken);
}

public interface IReportGeneratorRegistry
{
    IReportGenerator? Find(string code);
}

public sealed class ReportGeneratorRegistry(IEnumerable<IReportGenerator> generators) : IReportGeneratorRegistry
{
    public IReportGenerator? Find(string code) =>
        generators.FirstOrDefault(generator => string.Equals(generator.Code, code, StringComparison.Ordinal));
}
