using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Sniper.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists flips that a modifier can be applied to for profit",
    "This command is experimental and not all modifiers",
    "list correctly. It uses the median sniper flip finder",
    "to find price differences between modifiers on the ah")]
public class AttributeFlipCommand : ReadOnlyListCommand<AttributeFlip>
{
    protected override string Title => "Attribute craft Flips";

    protected override string NoMatchText => "No attribute crafts found";

    public override bool IsPublic => true;

    public AttributeFlipCommand()
    {
        sorters.Add("price", e => e.OrderByDescending(a => a.Target));
        sorters.Add("profit", e => e.OrderByDescending(a => a.ProfitAfterTax));
        sorters.Add("vol", e => e.OrderByDescending(a => a.Volume));
        sorters.Add("volume", e => e.OrderByDescending(a => a.Volume));
        sorters.Add("age", e => e.OrderByDescending(a => a.FoundAt));
    }

    public override async Task Execute(MinecraftSocket socket, string args)
    {
        if (!await socket.RequirePremium())
        {
            socket.Dialog(db => db.CoflCommand<PurchaseCommand>("Attribute flips are advanced craft like flips where you apply enchants, books, reforges etc to increase the value of an item and sel it for a profit.", null,
            "Because of how complex and advanced this is \nit is part of our premium offering"));
            return;
        }
        await base.Execute(socket, args);
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, AttributeFlip elem)
    {
        db.MsgLine($"{McColorCodes.GREEN}{elem.ItemName} {McColorCodes.RED}{socket.FormatPrice(elem.AuctionPrice)} {McColorCodes.GRAY}+{McColorCodes.RED}{socket.FormatPrice(elem.EstimatedCraftingCost)} {McColorCodes.RESET}to {McColorCodes.AQUA}{socket.FormatPrice(elem.Target)} {McColorCodes.RESET}apply:",
                $"/viewauction {elem.AuctionToBuy}",
                $"click to open the auction in question\n"
                + $"{McColorCodes.GRAY}do that before you buy the things to upgrade\n"
                + $"Estimated profit: {McColorCodes.AQUA}{socket.FormatPrice(elem.ProfitAfterTax)}"
                + $"\n{McColorCodes.GRAY}Volume: {elem.Volume}")
            .ForEach(elem.Ingredients, (db, ing) => db.MsgLine($"{McColorCodes.GRAY}- {McColorCodes.RESET}{ing.AttributeName}", null,
                $"This is estimated to cost {McColorCodes.AQUA}{socket.FormatPrice(ing.Price)}"));
    }

    protected override async Task<IEnumerable<AttributeFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var itemNamesTask = socket.GetService<Items.Client.Api.IItemsApi>().ItemNamesGetAsync();
        var service = socket.GetService<IAttributeApi>();
        var raw = await service.ApiAttributeCraftsGetWithHttpInfoAsync();
        var deserialized = JsonConvert.DeserializeObject<List<AttributeFlip>>(raw.RawContent);
        var names = (await itemNamesTask)?.ToDictionary(i => i.Tag, i => i.Name) ?? [];
        foreach (var item in deserialized.ToList())
        {
            if (NBT.IsPet(item.Tag) && (item.EndingKey.Tier > item.StartingKey.Tier || item.StartingKey.Modifiers.FirstOrDefault(k => k.Key == "exp").Value != item.EndingKey.Modifiers.FirstOrDefault(k => k.Key == "exp").Value))
            {
                deserialized.Remove(item); // remove pet exp or kat leveling
            }
            item.ItemName = BazaarUtils.GetSearchValue(item.Tag, names.GetValueOrDefault(item.Tag, item.Tag));
        }
        return deserialized;
    }

    protected override string GetId(AttributeFlip elem)
    {
        return elem.Tag + elem.EndingKey;
    }

}
