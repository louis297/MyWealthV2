using System.Text;
using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Commands.RequestReport;

[Authorize(Policy = Policies.ReportsCustomerCreate + "," + Policies.ReportsBookCreate)]
public class RequestReportCommand : IRequest<Guid>
{
    public Guid ReportId { get; set; }

    public JsonElement Parameters { get; set; }

    public string? Cron { get; set; }

    public bool? IsActive { get; set; }
}

public class RequestReportCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RequestReportCommand, Guid>
{
    public const int MaxParametersBytes = 8 * 1024;

    public async Task<Guid> Handle(RequestReportCommand request, CancellationToken cancellationToken)
    {
        if (request.Parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be an object.")
            ]);
        }

        if (Encoding.UTF8.GetByteCount(request.Parameters.GetRawText()) > MaxParametersBytes)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be 8 KB or smaller.")
            ]);
        }

        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var report = await db.Reports.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.ReportId, cancellationToken)
                     ?? throw new NotFoundException("Report", request.ReportId);

        var granted = await db.TenantReports.AsNoTracking()
            .AnyAsync(row => row.TenantId == tenantId && row.ReportId == report.Id, cancellationToken);
        var allowsManual = TenantReportGrantDto.ParseTriggers(report.AllowedTriggers)
            .Contains("manual", StringComparer.Ordinal);
        if (!report.IsActive || !granted || !allowsManual)
        {
            throw new NotFoundException("Report", report.PublicId);
        }

        var createPolicy = report.Scope == "Book"
            ? Policies.ReportsBookCreate
            : Policies.ReportsCustomerCreate;
        if (!RolePermissions.Has(role, createPolicy))
        {
            throw new ForbiddenAccessException();
        }

        if (report.Code == CustomerTransactionsBinder.Code)
        {
            CustomerTransactionsBinder.Validate(request.Parameters);
        }
        else
        {
            throw new NotFoundException("Report", report.PublicId);
        }

        throw new InvalidOperationException("Report request persistence is not implemented.");
    }
}
