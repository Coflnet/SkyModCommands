using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Sniper.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class AttributeFlipCommand : ReadOnlyListCommand<Sniper.Client.Model.AttributeFlip>
{
    protected override string Title => "Attribute craft Flips";

    protected override string NoMatchText => "No attribute crafts found";

    public override bool IsPublic => true;

    public AttributeFlipCommand()
    {
        sorters.Add("price", e => e.OrderByDescending(a => a.Target));
        sorters.Add("vol", e => e.OrderByDescending(a => a.Volume));
        sorters.Add("volume", e => e.OrderByDescending(a => a.Volume));
        sorters.Add("age", e => e.OrderByDescending(a => a.FoundAt));
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Sniper.Client.Model.AttributeFlip elem)
    {
        db.MsgLine($"{elem.Tag} to {socket.FormatPrice(elem.Target)} apply:", $"/viewauction {elem.AuctionToBuy}", $"click to open the auction in question\n{McColorCodes.GRAY}do that before you buy the things to upgrade")
            .ForEach(elem.Ingredients, (db, ing) => db.MsgLine($"- {ing.AttributeName}"));
    }

    protected override async Task<IEnumerable<Sniper.Client.Model.AttributeFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var service = socket.GetService<IAttributeApi>();
        var raw = await service.ApiAttributeCraftsGetWithHttpInfoAsync();
        return JsonConvert.DeserializeObject<List<Sniper.Client.Model.AttributeFlip>>(raw.RawContent);
    }

    protected override string GetId(Sniper.Client.Model.AttributeFlip elem)
    {
        return elem.Tag + elem.EndingKey;
    }
}
