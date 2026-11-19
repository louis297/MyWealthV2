using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Accounts;

/// <summary>Draws deposit references from a cryptographic random source (client-money-accounts R16).</summary>
public sealed class RandomDepositReferenceSource : IDepositReferenceSource
{
    public DepositReference Next() => DepositReference.Generate(CryptoRandomSymbols.Instance);
}
