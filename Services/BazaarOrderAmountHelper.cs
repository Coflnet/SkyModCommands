using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Items.Client.Model;

namespace Coflnet.Sky.ModCommands.Services;

public static class BazaarOrderAmountHelper
{
    public const int MaxNonStackableOrderAmount = 4;

    public static int GetSuggestedBuyOrderAmount(string itemTag, double sellPrice, ItemCategory? itemCategory = null)
    {
        var suggestedAmount = sellPrice < 100_000 ? 64
            : sellPrice > 5_000_000 ? 1
            : 4;
        return ClampOrderAmount(itemTag, suggestedAmount, itemCategory);
    }

    public static int ClampOrderAmount(string itemTag, int amount, ItemCategory? itemCategory = null)
    {
        if (amount <= 0)
            return amount;

        return IsLikelyNonStackable(itemTag, itemCategory)
            ? Math.Min(amount, MaxNonStackableOrderAmount)
            : amount;
    }

    public static bool IsLikelyNonStackable(string itemTag, ItemCategory? itemCategory = null)
    {
        if (itemCategory == ItemCategory.REFORGE_STONE)
            return true;

        if (string.IsNullOrWhiteSpace(itemTag))
            return false;

        // Stack size metadata is not exposed, so fall back to known non-stackable heuristics.
        return itemTag.Contains("BOOK", StringComparison.OrdinalIgnoreCase) || itemTag.Contains("ENCHANTMENT_", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<ItemCategory?> GetKnownItemCategory(string itemTag, FilterStateService filterStateService)
    {
        if (string.IsNullOrWhiteSpace(itemTag) || filterStateService == null)
            return null;

        if (!filterStateService.State.itemCategories.TryGetValue(ItemCategory.REFORGE_STONE, out var reforgeStoneTags))
        {
            await filterStateService.GetItemCategory(ItemCategory.REFORGE_STONE);
            if (!filterStateService.State.itemCategories.TryGetValue(ItemCategory.REFORGE_STONE, out reforgeStoneTags))
                return null;
        }

        return reforgeStoneTags.Contains(itemTag) ? ItemCategory.REFORGE_STONE : null;
    }
}