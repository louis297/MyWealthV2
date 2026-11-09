using System.Text.Json;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Reads the per-leg transaction item shape (leg-accounts §8.1).</summary>
internal static class TransactionJson
{
    public static IReadOnlyList<JsonElement> CashLegs(JsonElement item) => Legs(item, "cash");

    public static IReadOnlyList<JsonElement> SecurityLegs(JsonElement item) => Legs(item, "security");

    public static JsonElement SingleCash(JsonElement item) => CashLegs(item).ShouldHaveSingleItem();

    private static IReadOnlyList<JsonElement> Legs(JsonElement item, string name)
    {
        item.TryGetProperty(name, out var legs).ShouldBeTrue($"item has no \"{name}\" array");
        legs.ValueKind.ShouldBe(JsonValueKind.Array);
        return legs.EnumerateArray().ToList();
    }
}
