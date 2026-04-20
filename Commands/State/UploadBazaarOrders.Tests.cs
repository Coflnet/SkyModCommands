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