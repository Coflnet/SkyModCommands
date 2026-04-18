using NUnit.Framework;

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
    public void GetSuggestedBuyOrderAmountKeepsExpensiveOrdersAt1()
    {
        Assert.That(BazaarOrderAmountHelper.GetSuggestedBuyOrderAmount("WHEAT", 6_000_000), Is.EqualTo(1));
    }
}