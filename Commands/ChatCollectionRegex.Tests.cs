using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Tests for the chat collection regex
    /// </summary>
    public class ChatCollectionRegexTests
    {
        Regex regex = new Regex(InventoryModSession.DefaultChatRegex);

        [Test]
        public void MatchesAuctionCreation()
        {
            Assert.IsTrue(regex.IsMatch("BIN Auction started for ◆ Hot Rune I!"));
        }

        [Test]
        public void MatchesAuctionCancel()
        {
            Assert.IsTrue(regex.IsMatch("You cancelled your auction for ◆ Hot Rune I!"));
        }

        [Test]
        public void PurchaseAuction()
        {
            Assert.IsTrue(regex.IsMatch("You purchased ◆ Hot Rune I for 1 coins!"));
        }

        [Test]
        public void BazaarCancel()
        {
            Assert.IsTrue(regex.IsMatch("[Bazaar] Cancelled! Refunded 52x Green Candy from cancelling Sell Offer!"));
        }
    }
}
