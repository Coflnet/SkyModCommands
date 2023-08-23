using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;
using Moq;
using System.Linq;

namespace Coflnet.Sky.Commands.MC;
public class ModVersionAdapterTests
{
    public class TestModVersionAdapter : ModVersionAdapter
    {
        public TestModVersionAdapter(IMinecraftSocket socket) : base(socket)
        {
        }
        public List<ChatPart[]> Messages = new List<ChatPart[]>();
        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            SendMessage((await GetMessageparts(flip)).ToArray());
            return true;
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            Messages.Add(parts);
        }
    }

    [TestCase("o[menu]FLIP[sellerbtn]xy", "o| ✥ |FLIP|§7 sellers ah|xy")]
    [TestCase("Normal", "Normal|§7 sellers ah| ✥ ")]
    public async Task MenuReplace(string formatted, string result)
    {
        var socket = new Mock<IMinecraftSocket>();
        var settings = new FlipSettings()
        {
            Visibility = new() { Lore = true, SellerOpenButton = true },
            ModSettings = new() { Format = "" }
        };
        socket.SetupGet(s => s.Settings).Returns(settings);
        var flipCon = new Mock<IFlipConnection>();
        flipCon.SetupGet(con => con.Settings).Returns(settings);
        socket.SetupGet(s => s.formatProvider).Returns(new FormatProvider(flipCon.Object));
        socket.Setup(s => s.GetFlipMsg(It.IsAny<FlipInstance>())).Returns(formatted);
        var adapter = new TestModVersionAdapter(socket.Object);
        await adapter.SendFlip(new FlipInstance()
        {
            Auction = new() { StartingBid = 2500000000 },
            MedianPrice = 2500000000,
            Context = new()
        });
        Assert.AreEqual(1, adapter.Messages.Count);
        Assert.AreEqual(result, string.Join("|", adapter.Messages[0].Select(p => p.text)));

    }
}