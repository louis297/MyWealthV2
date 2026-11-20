using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>Deposit matching R30, R33: undo a match by reversing its deposit, while the account holds the cash.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class UnmatchStatementLineCommand : IRequest
{
    public const string CashGoneMessage =
        "The account no longer holds this cash, so the deposit cannot be undone. Correct it with an adjustment.";

    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

public class UnmatchStatementLineCommandHandler : IRequestHandler<UnmatchStatementLineCommand>
{
    public Task Handle(UnmatchStatementLineCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
