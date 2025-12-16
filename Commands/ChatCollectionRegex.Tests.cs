using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Tests for the chat collection regex
    /// </summary>
    public class ChatCollectionRegexTests
    {
        private Regex regex = new Regex(Shared.PrivacySettings.DefaultChatRegex);

        [TestCase("[NPC] Kat: You can pick it up in 59 minutes 22 seconds")]
        [TestCase("[NPC] Kat: I'm currently taking care of your Bee!")]
        [TestCase("[NPC] Kat: Stop by once it\u0027s done, okay?")]
        public void MatchesKatMessage(string message)
        {
            Assert.That(regex.IsMatch(message));
        }

        [Test]
        public void MatchesAuctionCreation()
        {
            Assert.That(regex.IsMatch("BIN Auction started for ◆ Hot Rune I!"));
        }

        [Test]
        public void MatchesAuctionCancel()
        {
            Assert.That(regex.IsMatch("You cancelled your auction for ◆ Hot Rune I!"));
        }

        [Test]
        public void PurchaseAuction()
        {
            Assert.That(regex.IsMatch("You purchased ◆ Hot Rune I for 1 coins!"));
        }

        [Test]
        public void BazaarCancel()
        {
            Assert.That(regex.IsMatch("[Bazaar] Cancelled! Refunded 52x Green Candy from cancelling Sell Offer!"));
        }
        [Test]
        public void PlaceVerificationBid()
        {
            Assert.That(regex.IsMatch("Bid of 197 coins placed for Stick!"));
        }
        [Test]
        public void NoBlocksInTheWay()
        {
            Assert.That(!regex.IsMatch("There are blocks in the way!"));
        }
        [Test]
        public void TriggerReward()
        {
            Assert.That(regex.IsMatch("\nClick th' link t' visit our website an' plunder yer treasure: https://rewards.hypixel.net/claim-reward/225a264d"));
        }
    }
}
