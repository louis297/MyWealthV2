using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Common.Interfaces;

/// <summary>
/// Draws a new deposit reference for a Custodial account (client-money-accounts R16). The Infrastructure source
/// draws from a cryptographic random source; tests may replace it to prove the redraws of R17.
/// </summary>
public interface IDepositReferenceSource
{
    DepositReference Next();
}
