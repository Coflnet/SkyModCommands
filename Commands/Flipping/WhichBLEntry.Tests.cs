using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC
{
    public class WhichBLEntryTests
    {
        [Test]
        public void MatchWithTag()
        {
            Assert.IsTrue(WhichBLEntryCommand.Matches(
                new LowPricedAuction() { Auction = new SaveAuction() { Tag = "test" } }, 
                new ListEntry() { ItemTag = "test" }));
            Assert.IsFalse(WhichBLEntryCommand.Matches(
                new LowPricedAuction() { Auction = new SaveAuction() { Tag = "other" } }, 
                new ListEntry() { ItemTag = "test" }));
        }
        [Test]
        public void MatchWithFilter()
        {
            Assert.IsFalse(WhichBLEntryCommand.Matches(new LowPricedAuction() { 
                Auction = new SaveAuction() { Tag = "test" } }, 
                new ListEntry() { filter = new() { { "Reforge", "Giant" } } }));
        }
    }

}