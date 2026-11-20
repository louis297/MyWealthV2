namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// Finds the deposit references a payer may have typed in a bank reference text (deposit-matching R15).
/// </summary>
public static class DepositReferenceScanner
{
    /// <summary>Every distinct valid deposit reference in <paramref name="referenceText"/>, in the order found.</summary>
    public static IReadOnlyList<DepositReference> Candidates(string? referenceText)
    {
        if (string.IsNullOrWhiteSpace(referenceText))
        {
            return [];
        }

        // Each part between characters that are not ASCII letters or digits, then the whole text without them.
        var parts = new List<string>();
        var whole = new System.Text.StringBuilder(referenceText.Length);
        var current = new System.Text.StringBuilder();
        foreach (var ch in referenceText)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                current.Append(ch);
                whole.Append(ch);
                continue;
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        parts.Add(whole.ToString());

        var found = new List<DepositReference>();
        foreach (var part in parts)
        {
            if (part.Length != DepositReference.Length)
            {
                continue;
            }

            if (DepositReference.TryParse(Normalise(part), out var reference) && !found.Contains(reference))
            {
                found.Add(reference);
            }
        }

        return found;
    }

    /// <summary>Upper case, with the Crockford reading of <c>O</c> as <c>0</c> and <c>I</c>, <c>L</c> as <c>1</c>.</summary>
    private static string Normalise(string part)
    {
        var chars = part.ToUpperInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other
            };
        }

        return new string(chars);
    }
}
