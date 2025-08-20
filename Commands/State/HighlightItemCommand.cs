using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class HighlightItemCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var service = socket.GetService<Shared.SettingsService>();
        var currentSettings = await service.GetCurrentValue<Coflnet.Sky.Api.Models.Mod.DescriptionSetting>(socket.UserId, "description", () =>
                {
                    return Coflnet.Sky.Api.Models.Mod.DescriptionSetting.Default;
                });
        var itemResult = Convert<SearchCommand.ItemLocation>(arguments);
        currentSettings.HighlightInfo = new Coflnet.Sky.Api.Models.Mod.HighlightInfo()
        {
            Chestname = itemResult.Chestname,
            Position = itemResult.Position == null ? null : new Core.BlockPos()
            {
                X = itemResult.Position.X,
                Y = itemResult.Position.Y,
                Z = itemResult.Position.Z
            },
            HexColor = "00FF00",
            SlotId= itemResult.SlotId
        };
        await service.UpdateSetting(socket.UserId, "description", currentSettings);
        Console.WriteLine(JsonConvert.SerializeObject(currentSettings.HighlightInfo));

        if (itemResult.CommandToOpen != null)
        {
            socket.ExecuteCommand(itemResult.CommandToOpen);
        }
        if (itemResult.Position != null)
        {
            await Task.Delay(800); // Wait for the world to load
            socket.Dialog(db => db.Msg($"Found {itemResult.Item.ItemName} in {itemResult.Chestname} at {itemResult.Position.X}, {itemResult.Position.Y}, {itemResult.Position.Z}"));
            socket.Send(Response.Create("highlightBlocks", new BlockPos[] { new() { X = itemResult.Position.X, Y = itemResult.Position.Y, Z = itemResult.Position.Z } }));
            await Task.Delay(2500); // Wait for the world to load
            socket.Send(Response.Create("highlightBlocks", new BlockPos[] { new() { X = itemResult.Position.X, Y = itemResult.Position.Y, Z = itemResult.Position.Z } }));
        }
    }
}
