using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

#pragma warning disable CS0101 
public class FormatProviderTests
{
    [Test]
    public void UserFlipProfit()
    {
        var flipCon = new Mock<IFlipConnection>();
        flipCon.SetupGet(con => con.Settings).Returns(new Shared.FlipSettings() { Visibility = new() { Profit = true } });
        var provider = new FormatProvider(flipCon.Object);
        var output = provider.FormatFlip(new Shared.FlipInstance()
        {
            Auction = new() { StartingBid = 2500000000 },
            MedianPrice = 2500000000
        });
        Assert.AreEqual("\nFLIP:  §82,500,000,000 -> 2,500,000,000 (+-87,500,032) ", output);
    }


    [Test]
    public void CustomFormatRR()
    {
        var flipCon = new Mock<IFlipConnection>();
        flipCon.SetupGet(con => con.Settings).Returns(new Shared.FlipSettings()
        {
            ModSettings = new()
            {
                Format = "§8{0} -> {1} {11} "
            }
        });
        var provider = new FormatProvider(flipCon.Object);
        var output = provider.FormatFlip(new Shared.FlipInstance()
        {
            Auction = new() { StartingBid = 2500000000, Context = new() { { "pre-api", "123" } } },
            MedianPrice = 2500000000,
            Context = new() { { "isRR", "123" } }
        });
        Assert.AreEqual("§8FLIP ->  §cPRE-RR ", output);
    }

    [TestCase(1,"1")]
    [TestCase(-1_200_000,"-1.2M")]
    [TestCase(12_200_000_000,"12.2B")]
    [TestCase(1_000_000,"1M")]
    [TestCase(999_999.1,"1M")]
    [TestCase(105031d,"105K")]
    public void FormatNumberSort(double input, string expected)
    {
        Assert.AreEqual(expected, FormatProvider.FormatPriceShort(input));
    }

}