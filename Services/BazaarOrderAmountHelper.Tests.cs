using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;
using Coflnet.Sky.Items.Client.Model;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarOrderAmountHelperTests
{
    [TestCase("ENCHANTED_BOOK", 12, 4)]
    [TestCase("ENCHANTMENT_SHARPNESS_7", 12, 4)]
    [TestCase("POLARVOID_BOOK", 5, 4)]
    [TestCase("WHEAT", 12, 12)]
    [TestCase("", 12, 12)]
    public void ClampOrderAmountOnlyCapsLikelyNonStackables(string itemTag, int amount, int expected)
    {
        Assert.That(BazaarOrderAmountHelper.ClampOrderAmount(itemTag, amount), Is.EqualTo(expected));
    }

    [TestCase("RAW_REFORGE_STONE", ItemCategory.REFORGE_STONE, 12, 4)]
    [TestCase("WHEAT", ItemCategory.UNKNOWN, 12, 12)]
    public void ClampOrderAmountUsesItemCategoryForReforgeStones(string itemTag, ItemCategory itemCategory, int amount, int expected)
    {
        Assert.That(BazaarOrderAmountHelper.ClampOrderAmount(itemTag, amount, itemCategory), Is.EqualTo(expected));
    }

    [Test]
    public void GetSuggestedBuyOrderAmountKeepsCheapStackablesAt64()
    {
        Assert.That(BazaarOrderAmountHelper.GetSuggestedBuyOrderAmount("WHEAT", 50_000), Is.EqualTo(64));
    }

    [Test]
    public void GetSuggestedBuyOrderAmountCapsCheapBooksAt4()
    {
        Assert.That(BazaarOrderAmountHelper.GetSuggestedBuyOrderAmount("ENCHANTED_BOOK", 50_000), Is.EqualTo(4));
    }

    [Test]
    public void GetSuggestedBuyOrderAmountCapsCheapReforgeStonesAt4()
    {
        Assert.That(BazaarOrderAmountHelper.GetSuggestedBuyOrderAmount("RAW_REFORGE_STONE", 50_000, ItemCategory.REFORGE_STONE), Is.EqualTo(4));
    }

    [Test]
    public async Task GetKnownItemCategoryUsesLoadedFilterStateCache()
    {
        var filterStateService = new FilterStateService(null, null, null);
        filterStateService.State.itemCategories[ItemCategory.REFORGE_STONE] = new HashSet<string> { "RAW_REFORGE_STONE" };

        var itemCategory = await BazaarOrderAmountHelper.GetKnownItemCategory("RAW_REFORGE_STONE", filterStateService);

        Assert.That(itemCategory, Is.EqualTo(ItemCategory.REFORGE_STONE));
    }

    [Test]
    public void GetSuggestedBuyOrderAmountKeepsExpensiveOrdersAt1()
    {
        Assert.That(BazaarOrderAmountHelper.GetSuggestedBuyOrderAmount("WHEAT", 6_000_000), Is.EqualTo(1));
    }
}