using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.StatementLines.Queries.GetStatementLines;

/// <summary>Deposit matching R21, R22: the current tenant's statement lines, newest first.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed record GetStatementLinesQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? Currency = null,
    Guid? ClientMoneyAccountId = null,
    Guid? TenantId = null,
    string? Direction = null) : IRequest<PagedList<StatementLineDto>>
{
    /// <summary>The status names in <see cref="Status"/>, comma-separated; empty when there is no filter.</summary>
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? []
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public class GetStatementLinesQueryValidator : AbstractValidator<GetStatementLinesQuery>
{
    private static readonly string[] Names = Enum.GetNames<BankStatementLineStatus>();

    public GetStatementLinesQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetStatementLinesQuery.StatusNames(status)
                .All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
        RuleFor(query => query.Direction)
            .Must(direction => direction is null || Enum.TryParse<BankLineDirection>(direction, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed) && !int.TryParse(direction, out _))
            .WithMessage("Direction must be Credit or Debit.");
    }
}

public class GetStatementLinesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetStatementLinesQuery, PagedList<StatementLineDto>>
{
    public async Task<PagedList<StatementLineDto>> Handle(GetStatementLinesQuery request, CancellationToken cancellationToken)
    {
        var tenantId = StatementLineReader.CurrentTenantId(currentUser);
        var query = db.BankStatementLines.AsNoTracking().Where(row => row.TenantId == tenantId);

        var statuses = GetStatementLinesQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<BankStatementLineStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        // Withdrawals R44: receipts, payments out, or both.
        if (request.Direction is not null)
        {
            var direction = Enum.Parse<BankLineDirection>(request.Direction, ignoreCase: true);
            query = query.Where(row => row.Direction == direction);
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var currency = request.Currency.Trim().ToUpperInvariant();
            query = query.Where(row => row.Currency == currency);
        }

        if (request.ClientMoneyAccountId is Guid clientMoneyAccountId)
        {
            query = query.Where(row => db.ClientMoneyAccounts.Any(account =>
                account.Id == row.ClientMoneyAccountId && account.PublicId == clientMoneyAccountId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var lines = await query
            .OrderByDescending(row => row.ReceivedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<StatementLineDto>
        {
            Items = await StatementLineReader.ToDtosAsync(db, lines, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
