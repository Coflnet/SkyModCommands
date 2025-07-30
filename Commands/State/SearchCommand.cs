using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class SearchCommand : ReadOnlyListCommand<SearchCommand.ItemLocation>
{
    protected override void Format(MinecraftSocket socket, DialogBuilder db, ItemLocation elem)
    {
        db.CoflCommand<HighlightItemCommand>($"Found {McColorCodes.AQUA}{elem.Item.ItemName}{McColorCodes.GRAY} in {McColorCodes.RESET}{elem.Chestname}\n",
                JsonConvert.SerializeObject(elem),
                $"{elem.Item.Description}\n{McColorCodes.YELLOW}Click to\n{McColorCodes.RESET}{elem.Title}");
    }

    protected override async Task<IEnumerable<ItemLocation>> GetElements(MinecraftSocket socket, string val)
    {
        var stateApi = socket.GetService<IPlayerStateApi>();
        if(socket.SessionInfo?.ProfileId == null)
        {
            socket.Dialog(db => db.Msg("Please switch islands so we can read the profile id you are on from chat"));
            return [];
        }
        var allChests = await stateApi.PlayerStatePlayerIdStorageGetAsync(Guid.Parse(socket.SessionInfo.McUuid), Guid.Empty);
        return allChests.SelectMany(c => c.Items.Select((i,index) => new ItemLocation()
        {
            Chestname = c.Name,
            CommandToOpen = GetCommandForContainer(c).command,
            Title = GetCommandForContainer(c).title,
            Position = c.Position,
            Item = i,
            SlotId = index
        })).ToList();

        static (string command, string title) GetCommandForContainer(ChestView i)
        {
            if (i.Position != null)
                return ($"/warp home", "Warp to to your island and highlight chest containing item");
            if (i.Name == null)
                return (null, "Item found in inventory");
            if (i.Name.StartsWith("Ender Chest"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(i.Name, @"Ender Chest \((\d+)/3\)");
                var pageId = match.Success ? match.Groups[1].Value : "1";
                return ($"/ec {pageId}", $"Open Ender Chest page {pageId}");
            }
            if (i.Name.Contains("Backpack"))
            {
                // Jumbo Backpack (Slot #3)
                var match = System.Text.RegularExpressions.Regex.Match(i.Name, @"Backpack \(Slot #(\d+)\)");
                var slotId = match.Success ? match.Groups[1].Value : "1";
                return ($"/bp {slotId}", $"Open Backpack slot {slotId}");
            }
            if (i.Name == "Pets")
                return ("/pets", "Open Pets menu");
            if (i.Name.StartsWith("Wardrobe"))
                return ("/wardrobe", "Open Wardrobe menu");
            if (i.Name.StartsWith("Sack of"))
                return ("/sacks", "Open Sacks menu");
            return (null, $"Found in {McColorCodes.AQUA}{i.Name}{McColorCodes.RESET}\nbut don't know how to open that yet\nplease make a report in our discord");
        }
    }

    protected override string GetId(ItemLocation elem)
    {
        return elem.Item.ItemName + elem.Item.Description;
    }

    public class ItemLocation
    {
        public string Chestname { get; set; }
        public string CommandToOpen { get; set; }
        public Item Item { get; set; }
        public BlockPos Position { get; set; }
        public string Title { get; set; }
        public int SlotId { get; set; } = -1;
    }
}
