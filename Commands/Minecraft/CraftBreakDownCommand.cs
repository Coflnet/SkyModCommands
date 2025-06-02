using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;
using Item = Coflnet.Sky.PlayerState.Client.Model.Item;

namespace Coflnet.Sky.Commands.MC;

public class CraftBreakDownCommand : ItemSelectCommand<CraftBreakDownCommand>
{
    public override bool IsPublic => true;

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
        socket.Dialog(db => db.MsgLine("Breakdown:").ForEach(result.First().CraftPrice.GroupBy(c => c.Attribute).OrderBy(g => g.Sum(a => a.Price)), (db, r) =>
            db.MsgLine($" {McColorCodes.YELLOW}{r.Key} {McColorCodes.GRAY}costs {McColorCodes.GOLD}{socket.formatProvider.FormatPrice(r.Sum(c => c.Price))} coins", null,
            string.Join("\n", r.Select(c => NewMethod(socket, c)).Prepend("Required items summed:"))))
            .MsgLine($"Total cost: {McColorCodes.GOLD}{socket.formatProvider.FormatPrice(result.First().CraftPrice.Sum(c => c.Price))} coins"));

        static string NewMethod(MinecraftSocket socket, Api.Client.Model.CraftPrice c)
        {
            if(c.Price < 0)
            {
                return $"{McColorCodes.RED}{c.FormattedReson}{McColorCodes.GRAY} for {McColorCodes.GOLD}{McColorCodes.ITALIC}0/unknown coins";
            }
            return $"{McColorCodes.YELLOW}{c.FormattedReson}{McColorCodes.GRAY} for {McColorCodes.GOLD}{socket.formatProvider.FormatPrice(c.Price)} coins";
        }
    }
}