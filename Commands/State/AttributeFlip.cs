using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Sniper.Client.Api;
using Newtonsoft.Json;
using static Coflnet.Sky.Core.ItemReferences;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists flips that a modifier can be applied to for profit",
    "This command is experimental and not all modifiers",
    "list correctly. It uses the median sniper flip finder",
    "to find price differences between modifiers on the ah")]
public class AttributeFlipCommand : ReadOnlyListCommand<AttributeFlipCommand.AttributeFlip>
{
    protected override string Title => "Attribute craft Flips";

    protected override string NoMatchText => "No attribute crafts found";

    public override bool IsPublic => true;

    public AttributeFlipCommand()
    {
        sorters.Add("price", e => e.OrderByDescending(a => a.Target));
        sorters.Add("profit", e => e.OrderByDescending(a => a.Target - a.EstimatedCraftingCost - a.AuctionPrice));
        sorters.Add("vol", e => e.OrderByDescending(a => a.Volume));
        sorters.Add("volume", e => e.OrderByDescending(a => a.Volume));
        sorters.Add("age", e => e.OrderByDescending(a => a.FoundAt));
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, AttributeFlip elem)
    {
        db.MsgLine($"{McColorCodes.GREEN}{elem.Tag} {McColorCodes.RED}{socket.FormatPrice(elem.AuctionPrice)} {McColorCodes.GRAY}+{McColorCodes.RED}{socket.FormatPrice(elem.EstimatedCraftingCost)} {McColorCodes.RESET}to {McColorCodes.AQUA}{socket.FormatPrice(elem.Target)} {McColorCodes.RESET}apply:",
                $"/viewauction {elem.AuctionToBuy}",
                $"click to open the auction in question\n"
                + $"{McColorCodes.GRAY}do that before you buy the things to upgrade\n"
                + $"Estimated profit: {McColorCodes.AQUA}{socket.FormatPrice(elem.Target - elem.EstimatedCraftingCost - elem.AuctionPrice)}"
                + $"\n{McColorCodes.GRAY}Volume: {elem.Volume}")
            .ForEach(elem.Ingredients, (db, ing) => db.MsgLine($"{McColorCodes.GRAY}- {McColorCodes.RESET}{ing.AttributeName}", null,
                $"This is estimated to cost {McColorCodes.AQUA}{socket.FormatPrice(ing.Price)}"));
    }

    protected override async Task<IEnumerable<AttributeFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var service = socket.GetService<IAttributeApi>();
        var raw = await service.ApiAttributeCraftsGetWithHttpInfoAsync();
        var deserialized = JsonConvert.DeserializeObject<List<AttributeFlip>>(raw.RawContent);
        foreach (var item in deserialized.ToList())
        {
            if (NBT.IsPet(item.Tag) && (item.EndingKey.Tier > item.StartingKey.Tier || item.StartingKey.Modifiers.FirstOrDefault(k => k.Key == "exp").Value != item.EndingKey.Modifiers.FirstOrDefault(k => k.Key == "exp").Value))
            {
                deserialized.Remove(item); // remove pet exp or kat leveling
            }
        }
        return deserialized;
    }

    protected override string GetId(AttributeFlip elem)
    {
        return elem.Tag + elem.EndingKey;
    }
    public class Ingredient
    {
        //
        // Summary:
        //     Gets or Sets ItemId
        [DataMember(Name = "itemId", EmitDefaultValue = true)]
        public string ItemId { get; set; }

        //
        // Summary:
        //     Gets or Sets AttributeName
        [DataMember(Name = "attributeName", EmitDefaultValue = true)]
        public string AttributeName { get; set; }

        //
        // Summary:
        //     Gets or Sets Amount
        [DataMember(Name = "amount", EmitDefaultValue = false)]
        public int Amount { get; set; }

        //
        // Summary:
        //     Gets or Sets Price
        [DataMember(Name = "price", EmitDefaultValue = false)]
        public double Price { get; set; }
    }

    public class Enchant
    {
        //
        // Summary:
        //     Gets or Sets Type
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public Enchantment.EnchantmentType? Type { get; set; }

        //
        // Summary:
        //     Gets or Sets Lvl
        [DataMember(Name = "lvl", EmitDefaultValue = false)]
        public int Lvl { get; set; }
    }
    public class AuctionKey
    {
        //
        // Summary:
        //     Gets or Sets Reforge
        [DataMember(Name = "reforge", EmitDefaultValue = false)]
        public Reforge? Reforge { get; set; }

        //
        // Summary:
        //     Gets or Sets Tier
        [DataMember(Name = "tier", EmitDefaultValue = false)]
        public Tier? Tier { get; set; }

        //
        // Summary:
        //     Gets or Sets Enchants
        [DataMember(Name = "enchants", EmitDefaultValue = true)]
        public List<Enchant> Enchants { get; set; }

        //
        // Summary:
        //     Gets or Sets Modifiers
        [DataMember(Name = "modifiers", EmitDefaultValue = true)]
        public List<Sniper.Client.Model.StringStringKeyValuePair> Modifiers { get; set; }

        //
        // Summary:
        //     Gets or Sets Count
        [DataMember(Name = "count", EmitDefaultValue = false)]
        public int Count { get; set; }

    }
    public class AttributeFlip
    {
        //
        // Summary:
        //     Gets or Sets Tag
        [DataMember(Name = "tag", EmitDefaultValue = true)]
        public string Tag { get; set; }

        //
        // Summary:
        //     Gets or Sets AuctionToBuy
        [DataMember(Name = "auctionToBuy", EmitDefaultValue = true)]
        public string AuctionToBuy { get; set; }
        public long AuctionPrice { get; set; }

        //
        // Summary:
        //     Gets or Sets Ingredients
        [DataMember(Name = "ingredients", EmitDefaultValue = true)]
        public List<Ingredient> Ingredients { get; set; }

        //
        // Summary:
        //     Gets or Sets StartingKey
        [DataMember(Name = "startingKey", EmitDefaultValue = false)]
        public AuctionKey StartingKey { get; set; }

        //
        // Summary:
        //     Gets or Sets EndingKey
        [DataMember(Name = "endingKey", EmitDefaultValue = false)]
        public AuctionKey EndingKey { get; set; }

        //
        // Summary:
        //     Gets or Sets Target
        [DataMember(Name = "target", EmitDefaultValue = false)]
        public long Target { get; set; }

        //
        // Summary:
        //     Gets or Sets EstimatedCraftingCost
        [DataMember(Name = "estimatedCraftingCost", EmitDefaultValue = false)]
        public long EstimatedCraftingCost { get; set; }

        //
        // Summary:
        //     Gets or Sets FoundAt
        [DataMember(Name = "foundAt", EmitDefaultValue = false)]
        public DateTime FoundAt { get; set; }

        //
        // Summary:
        //     Gets or Sets Volume
        [DataMember(Name = "volume", EmitDefaultValue = false)]
        public float Volume { get; set; }
    }
}
