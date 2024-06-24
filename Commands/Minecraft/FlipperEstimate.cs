using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;
using Item = Coflnet.Sky.PlayerState.Client.Model.Item;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Estimated item value from flipper flip finder")]
public class FlipperEstimateCommand : ItemSelectCommand<FlipperEstimateCommand>
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        await HandleSelectionOrDisplaySelect(socket, args, null, $"Select item to get the estimation for \n");
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string context, Item item)
    {
        // find last sell for item
        if (!item.ExtraAttributes.TryGetValue("uid", out var uid))
        {
            socket.Dialog(db => db.MsgLine("This item has no uid, so no references are available"));
            return;
        }
        var itemId = GetUidFromString(uid as string);
        using var scope = socket.GetService<IServiceScopeFactory>().CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<HypixelContext>();
        var key = NBT.Instance.GetKeyId("uid");
        var auctionUuid = db.Auctions
                    .Where(a => a.NBTLookup.Where(l => l.KeyId == key && l.Value == itemId).Any())
                    .OrderByDescending(a => a.End).Select(a => a.Uuid).FirstOrDefault();
        Activity.Current.Log($"Found auction {auctionUuid} for item {itemId}");
        if (auctionUuid == null)
        {
            socket.Dialog(db => db.MsgLine("No previous sell was found for this item"));
            return;
        }
        var references = await socket.GetService<Shared.FlipperService>().GetReferences(auctionUuid);
        if (references == null)
        {
            socket.Dialog(db => db.MsgLine("References could not be loaded"));
            return;
        }
        var median = references.OrderByDescending(r => r.HighestBidAmount).ElementAt(references.Count() / 2);
        Activity.Current.Log($"Found {references.Count()} references with a median of {median.HighestBidAmount} ({median.Uuid})");
        socket.Dialog(db => db.MsgLine($"The estimated value for this item is {median.HighestBidAmount} coins", null, $"https://sky.coflnet.com/auction/{median.Uuid}"));
    }

    private static long GetUidFromString(string u)
    {
        if (u.Length < 12)
            throw new CoflnetException("invalid_uuid", "One or more passed uuids are invalid (too short)");
        return NBT.UidToLong(u.Substring(u.Length - 12));
    }
}
