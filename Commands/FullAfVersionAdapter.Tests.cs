using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class FullAfVersionAdapterTests
{
    [TestCase("You must set it to at least 1,500,000!", 1_500_000)]
    [TestCase("You must set it to at least 500", 500)]
    public void TryExtractMinimumListingPriceParsesExpectedValue(string message, int expected)
    {
        Assert.That(FullAfVersionAdapter.TryExtractMinimumListingPrice(message, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("You must set it to at least !")]
    [TestCase(null)]
    public void TryExtractMinimumListingPriceRejectsInvalidMessages(string message)
    {
        Assert.That(FullAfVersionAdapter.TryExtractMinimumListingPrice(message, out _), Is.False);
    }
}