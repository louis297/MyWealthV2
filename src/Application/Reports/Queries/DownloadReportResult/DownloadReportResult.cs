using System.Text.Json;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Queries.DownloadReportResult;

[Authorize(Policy = Policies.ReportsCustomerRead + "," + Policies.ReportsBookRead)]
public record DownloadReportResultQuery(Guid Id) : IRequest<ReportPdf>;

public sealed record ReportPdf(byte[] Content);

public class DownloadReportResultHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReportFileStore files) : IRequestHandler<DownloadReportResultQuery, ReportPdf>
{
    public async Task<ReportPdf> Handle(DownloadReportResultQuery request, CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var execution = await db.ReportExecutions.AsNoTracking()
                            .SingleOrDefaultAsync(
                                row => row.PublicId == request.Id && row.TenantId == tenantId,
                                cancellationToken)
                        ?? throw new NotFoundException("ReportExecution", request.Id);
        var report = await db.Reports.AsNoTracking()
                         .SingleAsync(row => row.Id == execution.ReportId, cancellationToken);
        var readPolicy = report.Scope == "Book"
            ? Policies.ReportsBookRead
            : Policies.ReportsCustomerRead;
        if (!RolePermissions.Has(role, readPolicy))
        {
            throw new ForbiddenAccessException();
        }

        if (report.Scope == "Book")
        {
            if (role == UserRole.Adviser)
            {
                await AdviserReportScope.EnsureSnapshotAsync(
                    db, currentUser, execution.ParametersJson, "ReportExecution", cancellationToken);
            }
        }
        else if (role != UserRole.TenantAdmin)
        {
            await EnsureCustomerScopeAsync(execution.ParametersJson, tenantId, role, cancellationToken);
        }

        if (execution.Status != "Succeeded" || execution.ResultStorageKey is null)
        {
            throw new ReportNotReadyException();
        }

        var pdf = await files.ReadAsync(execution.ResultStorageKey, cancellationToken)
                  ?? throw new ReportNotReadyException();
        return new ReportPdf(pdf);
    }

    private async Task EnsureCustomerScopeAsync(
        string parametersJson,
        int tenantId,
        UserRole role,
        CancellationToken cancellationToken)
    {
        CustomerTransactionsBinding binding;
        try
        {
            using var parameters = JsonDocument.Parse(parametersJson);
            binding = CustomerTransactionsBinder.Bind(parameters.RootElement);
        }
        catch (JsonException)
        {
            throw new NotFoundException("ReportExecution", "parameters");
        }
        catch (ValidationException)
        {
            throw new NotFoundException("ReportExecution", "parameters");
        }

        var customer = await db.Users.AsNoTracking()
                           .SingleOrDefaultAsync(
                               row => row.PublicId == binding.CustomerId
                                      && row.TenantId == tenantId
                                      && row.Role == UserRole.Customer,
                               cancellationToken)
                       ?? throw new NotFoundException("Customer", binding.CustomerId);

        CustomerReportScope.Ensure(customer, role, currentUser);
    }
}
