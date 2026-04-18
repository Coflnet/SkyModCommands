using System;

namespace Coflnet.Sky.ModCommands.Services;

public static class BazaarOrderAmountHelper
{
    public const int MaxNonStackableOrderAmount = 4;

    public static int GetSuggestedBuyOrderAmount(string itemTag, double sellPrice)
    {
        var suggestedAmount = sellPrice < 100_000 ? 64
            : sellPrice > 5_000_000 ? 1
            : 4;
        return ClampOrderAmount(itemTag, suggestedAmount);
    }

    public static int ClampOrderAmount(string itemTag, int amount)
    {
        if (amount <= 0)
            return amount;

        return IsLikelyNonStackable(itemTag)
            ? Math.Min(amount, MaxNonStackableOrderAmount)
            : amount;
    }

    public static bool IsLikelyNonStackable(string itemTag)
    {
        if (string.IsNullOrWhiteSpace(itemTag))
            return false;

        // The current item APIs in this workspace do not expose stack size metadata,
        // so use the existing book-tag heuristic from bazaar recommendations centrally.
        return itemTag.Contains("BOOK", StringComparison.OrdinalIgnoreCase) || itemTag.Contains("ENCHANTMENT_", StringComparison.OrdinalIgnoreCase);
    }
}