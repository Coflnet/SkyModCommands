using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Newtonsoft.Json;
using Item = Coflnet.Sky.PlayerState.Client.Model.Item;

namespace Coflnet.Sky.Commands.MC;

public class CraftBreakDownCommand : ItemSelectCommand<CraftBreakDownCommand>
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        await HandleSelectionOrDisplaySelect(socket, args, null, $"Select item to get the cost for \n");
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string context, Item item)
    {
        // hack convert
        var converted = JsonConvert.DeserializeObject<Api.Client.Model.ItemRepresent>(JsonConvert.SerializeObject(item));
        Activity.Current.Log(JsonConvert.SerializeObject(converted));
        var result = await socket.GetService<IModApi>().ApiModPricingBreakdownPostAsync(new() { converted });
        socket.Dialog(db => db.MsgLine("Breakdown:").ForEach(result.First().CraftPrice, (db, r) => db.MsgLine($"{r.Attribute}: {r.FormattedReson} {r.Count}x {r.Price} coins")));
    }
}