using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC;

public abstract class ItemSelectCommand<T> : McCommand where T : ItemSelectCommand<T>
{
    protected async Task HandleSelectionOrDisplaySelect(MinecraftSocket socket, string[] args, string context, string hoverPrefix)
    {
        if(socket.SessionInfo.McName == null)
            throw new CoflnetException("not logged in", "Your minecraft account cou");
        var inventory = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLastChestGetAsync(socket.SessionInfo.McName);
        if (args.Length > 1)
        {
            var index = int.Parse(args.Last());
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


    protected abstract Task SelectedItem(MinecraftSocket socket, string context, Item item);


    private static string GetInventoryRepresent(MinecraftSocket socket, Item item)
    {
        if (string.IsNullOrWhiteSpace(item.ItemName))
            return "§7[   ] ";
        var rarityColor = item.ItemName.StartsWith("§") ? item.ItemName.Substring(0, 2) : "§7";
        var name = Regex.Replace(item.ItemName, @"§[\da-f]", "");
        System.Console.WriteLine(name);
        return $"{rarityColor}[{name.Truncate(2)}] ";
    }
}