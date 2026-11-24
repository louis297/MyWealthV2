using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Accounts.Commands.SetDividendReinvestment;

/// <summary>Dividends R17: the standing reinvestment election of a Custodial account.</summary>
[Authorize(Policy = Policies.AccountsManage)]
public sealed class SetAccountDividendReinvestmentCommand : IRequest
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public bool? Reinvest { get; init; }

    public string? RowVersion { get; init; }
}

public class SetAccountDividendReinvestmentCommandHandler
    : IRequestHandler<SetAccountDividendReinvestmentCommand>
{
    public Task Handle(SetAccountDividendReinvestmentCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
