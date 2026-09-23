using System.Linq;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Tests for NpcCommand display logic.
///
/// Background: The live sky-crafts service returns hourlySells=0 for all NPC flip items.
/// Root cause: NpcSellService.cs catches a silent exception from bazaarApi.GetAllPricesAsync(),
/// leaving the prices dictionary empty and causing every item to get HourlySells = 0.
///
/// These tests confirm the SkyModCommands display code is correct — the bug is server-side
/// in the SkyCrafts service.
/// </summary>
public class NpcCommandTests
{
    // Sample data matching the live bazaar: ENCHANTED_CLAY_BLOCK has dailySellVolume=268669 → 11194/h
    private static readonly NpcFlip SampleFlip = new()
    {
        ItemId = "ENCHANTED_CLAY_BLOCK",
        ItemName = "Enchanted Clay Block",
        BuyPrice = 76450.7,
        NpcSellPrice = 76800,
        HourlySells = 11155  // expected value: dailySellVolume / 24 ≈ 11155
    };

    [Test]
    public void Format_WithNonZeroHourlySells_ShowsCorrectHourlyProfit()
    {
        var socket = new MinecraftSocket();
        var expectedProfit = socket.FormatPrice((SampleFlip.NpcSellPrice - SampleFlip.BuyPrice) * SampleFlip.HourlySells);
        var (_, parts) = InvokeFormat(SampleFlip);

        // Main line: " §6Enchanted Clay Block §7for §a<profit>/h §e[Buy]"
        var mainText = parts.Select(p => p.text).Single(t => t?.Contains("/h") == true);
        Assert.That(mainText, Does.Not.Contain("0/h"),
            "When hourlySells=11155, hourly profit must be non-zero");
        Assert.That(mainText, Does.Contain(expectedProfit),
            $"Hourly profit in text should be {expectedProfit}");
    }

    [Test]
    public void Format_WithNonZeroHourlySells_ShowsCorrectSellsPerHourInHover()
    {
        var socket = new MinecraftSocket();
        var expectedSellsPerHour = socket.FormatPrice(SampleFlip.HourlySells);
        var (_, parts) = InvokeFormat(SampleFlip);

        var hover = parts.Single(p => p.hover?.Contains("Sells/Hour") == true).hover;
        Assert.That(hover, Does.Contain(expectedSellsPerHour),
            $"Hover tooltip must show Sells/Hour: {expectedSellsPerHour}");
    }

    [Test]
    public void Format_WhenHourlySellsIsZero_ShowsZeroPerHour()
    {
        // Reproduces the current broken server behavior:
        // sky-crafts returns hourlySells=0 because bazaarApi.GetAllPricesAsync()
        // throws silently (prices dict stays empty) → every item gets HourlySells=0.
        var brokenFlip = new NpcFlip
        {
            ItemId = "ENCHANTED_CLAY_BLOCK",
            ItemName = "Enchanted Clay Block",
            BuyPrice = 76450.7,
            NpcSellPrice = 76800,
            HourlySells = 0  // ← what the live server currently returns
        };

        var (socket, parts) = InvokeFormat(brokenFlip);

        var mainText = parts.Select(p => p.text).Single(t => t?.Contains("/h") == true);
        Assert.That(mainText, Does.Contain("0/h"),
            "When hourlySells=0 (current live server bug), the display shows 0/h");
    }

    private static (MinecraftSocket socket, ChatPart[] parts) InvokeFormat(NpcFlip flip)
    {
        var socket = new MinecraftSocket();
        var db = new SocketDialogBuilder(socket);
        new TestableNpcCommand().InvokeFormat(socket, db, flip);
        return (socket, db.Build());
    }

    private class TestableNpcCommand : NpcCommand
    {
        public void InvokeFormat(MinecraftSocket socket, DialogBuilder db, NpcFlip elem)
            => Format(socket, db, elem);
    }
}
