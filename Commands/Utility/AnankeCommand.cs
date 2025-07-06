using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class AnankeCommand : ReadOnlyListCommand<AnankeCommand.Element>
{
    public override bool IsPublic => true;
    private static Dictionary<string, double> costs = new()
    {
        { "T7_BOOK", 5.00 },
        { "SCYTHE_BLADE", 0.98 },
        { "SHARD_OF_THE_SHREDDED", 1.84 },
        { "WARDEN_HEART", 7.40 },
        { "OVERFLUX_CAPACITOR", 4.10 },
        { "JUDGEMENT_CORE", 12.64 },
        { "ARTIFACT_UPGRADER", 25.29 },
        { "BURGER", 2.31 },
        { "VAMPIRE_PART", 2.31 },
        { "THE_ONE_IV", 1.56 },
        { "HIGH_CLASS_DICE", 2.29 },
        { "FIRST_MASTER_STAR", 0.98 },
        { "SECOND_MASTER_STAR", 0.97 },
        { "THIRD_MASTER_STAR", 2.45 },
        { "FOURTH_MASTER_STAR", 4.06 },
        { "FIFTH_MASTER_STAR", 20.29 },
        { "SHADOW_FURY", 2.86 },
        { "GIANTS_SWORD", 5.33 },
        { "PRECURSOR_EYE", 8.89 },
        { "NECRON_HANDLE", 33.79 },
        { "SCROLL_F7", 26.38 },
        { "IMPLOSION_SCROLL", 24.33 },
        { "SHADOW_WARP_SCROLL", 24.33 },
        { "WITHER_SHIELD_SCROLL", 24.33 },
        { "DARK_CLAYMORE", 60.83 },
        { "QUICK_CLAW", 26.47 },
        { "DIVANS_ALLOY", 58.82 },
        { "DYE_NADESHIKO", 25 },
        { "DYE_MATCHA", 150 },
        { "DYE_CELESTE", 250 },
        { "DYE_BYZANTIUM", 1.072 },
        { "DYE_SANGRIA", 94 },
        { "DYE_FLAME", 883 },
        { "DYE_LIVID", 50 },
        { "DYE_NECRON", 143 },
        { "DYE_JADE", 295 },
    };

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Element item)
    {
        db.MsgLine($" {item.Tag} {McColorCodes.GRAY}for {McColorCodes.AQUA}{item.Price/item.FeathersRequired} coins per feather",
                    "https://sky.coflnet.com/item/" + item.Tag,
                    $"Requires {McColorCodes.AQUA}{item.FeathersRequired} feathers{McColorCodes.GRAY}, total cost: {McColorCodes.AQUA}{socket.FormatPrice(item.Cost)} coins\n"
                    + $"Estimated profit buying at ah: {McColorCodes.AQUA}{socket.FormatPrice(item.Price-item.Cost)} coins"
                    + "Click to check history on website");
    }

    protected override async Task<IEnumerable<Element>> GetElements(MinecraftSocket socket, string val)
    {
        var cleanPrices = await socket.GetService<ISniperClient>().GetCleanPrices();
        var all = new List<Element>();
        var featherPrice = cleanPrices.GetValueOrDefault("ANANKE_FEATHER");
        foreach (var item in costs)
        {
            if (!cleanPrices.TryGetValue(item.Key, out var price))
            {
                Console.WriteLine($"Price for {item.Key} not found in clean prices, skipping.");
                continue;
            }
            var feathersRequired = item.Value;
            var costOfFeathers = featherPrice * ((int)feathersRequired + 1);
            all.Add(new Element
            {
                Tag = item.Key,
                Cost = costOfFeathers,
                FeathersRequired = feathersRequired,
                Price = price
            });
        }
        return all;
    }

    protected override string GetId(Element elem)
    {
        return elem.Tag;
    }

    public class AnankeCost
    {
        public string Tag { get; set; }
        public double FeathersRequired { get; set; }
    }
    public class Element
    {
        public string Tag { get; set; }
        public long Price { get; set; }
        public long Cost { get; set; }
        public double FeathersRequired { get; set; }
        public string AuctionUuid { get; set; }
    }
}
