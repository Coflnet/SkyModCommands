using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;
using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC;

public abstract class ItemSelectCommand<T> : McCommand where T : ItemSelectCommand<T>
{
    protected async Task HandleSelectionOrDisplaySelect(MinecraftSocket socket, string[] args, string context, string hoverPrefix)
    {
        if (socket.SessionInfo.McName == null)
            throw new CoflnetException("not logged in", "Your minecraft account could not be confirmed, please run /cofl verify");
        var inventory = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLastChestGetAsync(socket.SessionInfo.McName);
        if (args.Length > 1)
        {
            var index = int.Parse(args.Last());
            if (index < 0 || index >= inventory.Count)
            {
                Activity.Current.Log($"Invalid index {index} for {socket.SessionInfo.McName} in {context}");
                Activity.Current.Log(JsonConvert.SerializeObject(inventory));
                throw new CoflnetException("invalid index", $"The selected item at {index} was not found in inventory");
            }
            var item = inventory[index];
            if (item == null)
            {
                socket.SendMessage(new ChatPart("§cThere is no item in the selected slot"));
            }
            else
                await SelectedItem(socket, context, item);
        }
        else
            socket.Dialog(db => db.MsgLine("Select the item you want to share").ForEach(inventory.Batch(9),
                    (db, itemRow, i) => db.ForEach(itemRow, (db, item, j)
                        => db.CoflCommand<T>(
                            GetInventoryRepresent(socket, item),
                            $"{context} {i * 9 + j}",
                            $"{hoverPrefix}{item.ItemName}\n{item.Description}")).LineBreak()));
    }

    protected static SaveAuction ConvertToAuction(PlayerState.Client.Model.Item item)
    {
        var auction = new SaveAuction()
        {
            Tag = item.Tag,
            ItemName = item.ItemName,
            Enchantments = item.Enchantments?.Select(e => new Enchantment()
            {
                Type = Enum.Parse<Enchantment.EnchantmentType>(e.Key, true),
                Level = (byte)e.Value
            }).ToList() ?? [],
            Count = item.Count ?? 1,
        };
        auction.SetFlattenedNbt(NBT.FlattenNbtData(item.ExtraAttributes));
        auction.Tier = (Tier)int.Parse(item.ExtraAttributes.GetValueOrDefault("tier", 0).ToString());
        return auction;
    }

    protected abstract Task SelectedItem(MinecraftSocket socket, string context, PlayerState.Client.Model.Item item);


    private static string GetInventoryRepresent(MinecraftSocket socket, PlayerState.Client.Model.Item item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemName))
            return "§7[   ] ";
        var rarityColor = item.ItemName.StartsWith("§") ? item.ItemName.Substring(0, 2) : "§7";
        var name = Regex.Replace(item.ItemName, @"§[\da-f]", "");
        System.Console.WriteLine(name);
        return $"{rarityColor}[{name.Truncate(2)}] ";
    }
}