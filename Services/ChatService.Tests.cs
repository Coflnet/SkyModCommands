using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class ChatServiceTests
{
    [Test]
    public void ModeratorEmblemCanBeTurnedOnAndOff()
    {
        Assert.That(
            ChatService.GetPrefix(AccountTier.NONE, Emblems.ModeratorSymbol, true),
            Is.EqualTo(Emblems.ModeratorSymbol + " " + McColorCodes.GRAY));
        Assert.That(
            ChatService.GetPrefix(AccountTier.NONE, null, true),
            Is.EqualTo(McColorCodes.GRAY));
    }

    [Test]
    public void ModeratorEmblemIsNotRenderedForNonModerator()
    {
        Assert.That(
            ChatService.GetPrefix(AccountTier.PREMIUM, Emblems.ModeratorSymbol, false),
            Is.EqualTo(McColorCodes.DARK_GREEN));
        Assert.That(
            ChatService.GetPrefix(AccountTier.PREMIUM, McColorCodes.GREEN + "✦", false),
            Is.EqualTo(McColorCodes.GREEN + "✦ " + McColorCodes.DARK_GREEN));
    }
}
