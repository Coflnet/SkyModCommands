using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class BazaarUtilsTests
{
    [TestCase("SHARD_NESSIE", "nessie shard")]
    [TestCase("SHARD_KADA_KNIGHT", "kada knight shard")]
    public void GetSearchValueConvertsShardTagsWhenDisplayNameMissing(string tag, string expected)
    {
        Assert.That(BazaarUtils.GetSearchValue(tag, tag), Is.EqualTo(expected));
    }

    [Test]
    public void GetSearchValueKeepsExistingDisplayNameForShardTags()
    {
        Assert.That(BazaarUtils.GetSearchValue("SHARD_NESSIE", "Nessie Shard"), Is.EqualTo("Nessie Shard"));
    }
}