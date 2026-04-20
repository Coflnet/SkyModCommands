using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Models;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class UploadBazaarOrdersTests
{
    [Test]
    public async Task ExecuteIgnoresOrderOptionsSnapshotAndKeepsPreviousOrders()
    {
        var socket = new CaptureDialogSocket();
        socket.SessionInfo.BazaarOrders = new List<BazaarOrderInfo>
        {
            new()
            {
                ItemTag = "VOLCANIC_ROCK",
                ItemName = "Volcanic Rock",
                Side = BazaarOrderSide.Sell,
                Amount = 3,
                PricePerUnit = 2_999_999.7
            }
        };

        var command = new UploadBazaarOrders();
        await command.Execute(socket, CreateOrderOptionsSnapshot());

        Assert.That(socket.SessionInfo.BazaarOrders.Count, Is.EqualTo(1));
        Assert.That(socket.SessionInfo.BazaarOrders[0].ItemTag, Is.EqualTo("VOLCANIC_ROCK"));
        Assert.That(socket.DialogMessages.Any(m => m.Contains("Wrong bazaar order state uploaded")), Is.True);
    }

    [Test]
    public async Task ExecuteSyncsSentBazaarOrdersFromOverview()
    {
        var socket = new CaptureDialogSocket();
        socket.SessionInfo.SentBazaarOrders = new List<SentBazaarOrderInfo>
        {
            new() { ItemTag = "VOLCANIC_ROCK", ItemName = "Volcanic Rock", Side = BazaarOrderSide.Sell, PricePerUnit = 2999999.7, Amount = 3 },
            new() { ItemTag = "WHEAT", ItemName = "Wheat", Side = BazaarOrderSide.Buy, PricePerUnit = 25130.6, Amount = 400 }
        };

        var command = new UploadBazaarOrders();
        await command.Execute(socket, CreateOverviewSnapshot());

        Assert.That(socket.SessionInfo.SentBazaarOrders.Count, Is.EqualTo(1));
        Assert.That(socket.SessionInfo.SentBazaarOrders[0].ItemTag, Is.EqualTo("VOLCANIC_ROCK"));
        Assert.That(socket.SessionInfo.SentBazaarOrders[0].ConfirmedAt, Is.Not.Null);
    }

    private static string CreateOrderOptionsSnapshot()
    {
        var slots = Enumerable.Range(0, 32)
            .Select(index => (object)new
            {
                count = 1,
                displayName = string.Empty,
                displayNameColored = string.Empty,
                empty = false,
                name = "minecraft:black_stained_glass_pane",
                slot = index
            })
            .ToArray();
        slots[13] = new
        {
            count = 1,
            displayName = "Cancel Order",
            displayNameColored = "§cCancel Order",
            empty = false,
            lore = new[]
            {
                "§7You will be refunded §a81§7x §7items."
            },
            name = "minecraft:green_terracotta",
            slot = 13
        };

        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            botState = "ManagingOrders",
            open = true,
            slotCount = 72,
            slots
        });
    }

    private static string CreateOverviewSnapshot()
    {
        var slots = Enumerable.Range(0, 72)
            .Select(index => (object)new
            {
                empty = true,
                slot = index
            })
            .ToArray();
        slots[10] = new
        {
            count = 1,
            displayName = "SELL Volcanic Rock",
            displayNameColored = "§6§lSELL §5§cVolcanic Rock",
            empty = false,
            lore = new[]
            {
                "§8Worth 8.9M coins",
                string.Empty,
                "§7Offer amount: §a3§7x",
                string.Empty,
                "§7Price per unit: §62,999,999.7 coins",
                string.Empty,
                "§7By: §a[VIP] Blexidon",
                string.Empty,
                "§eClick to view options!"
            },
            name = "minecraft:player_head",
            slot = 10,
            tag = "VOLCANIC_ROCK"
        };

        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            botState = "ManagingOrders",
            open = true,
            slotCount = 72,
            slots
        });
    }

    private class CaptureDialogSocket : MinecraftSocket
    {
        public List<string> DialogMessages { get; } = new();

        public override void Dialog(System.Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            var dialog = creation(new SocketDialogBuilder(this)).Build();
            DialogMessages.AddRange(dialog.Select(part => part.text));
        }
    }
}