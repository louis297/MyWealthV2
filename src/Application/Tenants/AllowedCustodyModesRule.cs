using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Tenants;

/// <summary>
/// The shape of <c>allowedCustodyModes</c> on <c>POST /tenants</c> and <c>PUT /tenants/{id}</c>
/// (tenant-custody-modes R3, R4): a non-empty list of exact custody mode names, each once.
/// </summary>
public static class AllowedCustodyModesRule
{
    public const string Message = "AllowedCustodyModes must list External, Custodial or both, each once.";

    private static readonly string[] Names = Enum.GetNames<CustodyMode>();

    public static IRuleBuilderOptions<T, IReadOnlyList<string>?> MustBeAllowedCustodyModes<T>(
        this IRuleBuilder<T, IReadOnlyList<string>?> rule) =>
        rule.Must(IsValid).WithMessage(Message);

    public static IReadOnlyCollection<CustodyMode> Parse(IReadOnlyList<string> modes) =>
        modes.Select(Enum.Parse<CustodyMode>).ToList();

    private static bool IsValid(IReadOnlyList<string>? modes) =>
        modes is { Count: > 0 }
        && modes.All(mode => mode is not null && Names.Contains(mode, StringComparer.Ordinal))
        && modes.Distinct(StringComparer.Ordinal).Count() == modes.Count;
}
