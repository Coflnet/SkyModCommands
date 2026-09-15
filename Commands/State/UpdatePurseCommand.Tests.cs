using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[TestFixture]
[SetCulture("en-US")]
public class UpdatePurseCommandTests
{
    [TestCase("Purse")]
    [TestCase("Piggy")]
    public async Task MalformedScoreboardPursePreservesBalanceAndUpdatesOtherState(string label)
    {
        var socket = new MinecraftSocket();
        socket.SessionInfo.Purse = 234567;
        var scoreboard = new[]
        {
            "SKYBLOCK",
            $"{label}: 111111:",
            "Bits: 10,920",
            "♲ Ironman",
            " ⏣ The Rift"
        };

        await new UploadScoreboardCommand().Execute(socket, JsonConvert.SerializeObject(scoreboard));

        Assert.That(socket.SessionInfo.Purse, Is.EqualTo(234567));
        Assert.That(socket.SessionInfo.Bits, Is.EqualTo(10920));
        Assert.That(socket.SessionInfo.IsIronman, Is.True);
        Assert.That(socket.SessionInfo.IsRift, Is.True);
    }

    [TestCase("Purse: 1,234,567.8 (+10)")]
    [TestCase("Piggy: 1,234,567.8")]
    public async Task ValidScoreboardPurseUpdatesBalance(string purseLine)
    {
        var socket = new MinecraftSocket();

        await new UploadScoreboardCommand().Execute(socket,
            JsonConvert.SerializeObject(new[] { "SKYBLOCK", purseLine }));

        Assert.That(socket.SessionInfo.Purse, Is.EqualTo(1234567));
    }

    [TestCase("123456", 123456)]
    [TestCase("\"123456.9\"", 123456)]
    [TestCase("1,234.9", 1234)]
    [TestCase("1.25e3", 1250)]
    [TestCase("0", 0)]
    public async Task ValidDirectPursePreservesSupportedFormats(string argument, long expected)
    {
        var socket = new MinecraftSocket();
        socket.SessionInfo.Purse = 234567;

        await new UpdatePurseCommand().Execute(socket, argument);

        Assert.That(socket.SessionInfo.Purse, Is.EqualTo(expected));
    }
}
