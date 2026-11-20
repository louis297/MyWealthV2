using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>Deposit matching R26: match an unmatched receipt to a Custodial account by hand.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class MatchStatementLineCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Guid? AccountId { get; init; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    /// <summary>Refused when present (R21).</summary>
    public Guid? TenantId { get; init; }
}

/// <summary>Deposit matching R27: money that is not a Customer's.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class IgnoreStatementLineCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Deposit matching R28: money that must go back to the payer.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class RequestStatementLineReturnCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Deposit matching R29: back into suspense.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class ReopenStatementLineCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    /// <summary>Refused when present: reopen takes no note (R25).</summary>
    public string? Note { get; init; }

    public Guid? TenantId { get; init; }
}

public class MatchStatementLineCommandHandler : IRequestHandler<MatchStatementLineCommand>
{
    public Task Handle(MatchStatementLineCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class IgnoreStatementLineCommandHandler : IRequestHandler<IgnoreStatementLineCommand>
{
    public Task Handle(IgnoreStatementLineCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class RequestStatementLineReturnCommandHandler : IRequestHandler<RequestStatementLineReturnCommand>
{
    public Task Handle(RequestStatementLineReturnCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class ReopenStatementLineCommandHandler : IRequestHandler<ReopenStatementLineCommand>
{
    public Task Handle(ReopenStatementLineCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
