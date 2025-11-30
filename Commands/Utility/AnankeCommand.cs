using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists the top potential flips for",
    "RNG meter progress granted by Ananke Feathers")]
public class AnankeCommand : ReadOnlyListCommand<AnankeCommand.Element>
{
    public override bool IsPublic => true;

    // Progress granted per feather for each RNG meter type
    private static Dictionary<string, double> progressPerFeather = new()
    {
        { "Experimentation Table", 100_000 },
        { "Revenant Horror", 500_000 },
        { "Tarantula Broodfather", 500_000 },
        { "Sven Packmaster", 300_000 },
        { "Voidgloom Seraph", 70_000 },
        { "Riftstalker Bloodfiend", 8_000 },
        { "Inferno Demonlord", 85_000 },
        { "Catacombs F1", 10_000 },
        { "Catacombs F2-F5", 40_000 },
        { "Catacombs F6", 30_000 },
        { "Catacombs F7", 8_000 },
        { "Catacombs M1", 7_000 },
        { "Catacombs M2-M3", 12_000 },
        { "Catacombs M4-M5", 20_000 },
        { "Catacombs M6-M7", 7_000 },
        { "Crystal Nucleus", 17_000 }
    };

    // Map items to their RNG meter types and experience requirements
    private static Dictionary<string, (string meterType, double expTarget, long unlockCost)> itemData = new()
    {
        // Slayer items
        { "SCYTHE_BLADE", ("Revenant Horror", 489_900, 0) },
        { "SHARD_OF_THE_SHREDDED", ("Revenant Horror", 918_562, 0) },
        { "WARDEN_HEART", ("Revenant Horror", 3_674_250, 0) },
        { "SEVERED_HAND", ("Revenant Horror", 1_049_785, 0) },
        { "REVENANT_CATALYST", ("Revenant Horror", 49_500, 0) },
        { "TARANTULA_SILK", ("Tarantula Broodfather", 3_456, 0) },
        { "TARANTULA_CATALYST", ("Tarantula Broodfather", 133_222, 0) },
        { "FLY_SWATTER", ("Tarantula Broodfather", 299_750, 0) },
        { "VIAL_OF_VENOM", ("Tarantula Broodfather", 351_325, 0) },
        { "PRIMORDIAL_EYE", ("Tarantula Broodfather", 3_513_250, 0) },
        { "ENSNARED_SNAIL", ("Tarantula Broodfather", 1_171_083, 0) },
        { "DIGESTED_MOSQUITO", ("Tarantula Broodfather", 702_650, 0) },
        { "TARANTULA_TALISMAN", ("Tarantula Broodfather", 234_216, 0) },
        { "SHRIVELED_WASP", ("Tarantula Broodfather", 351_325, 0) },
        { "SUMMONING_EYE", ("Voidgloom Seraph", 74_250, 0) },
        { "TRANSMISSION_TUNER", ("Voidgloom Seraph", 22_366, 0) },
        { "NULL_ATOM", ("Voidgloom Seraph", 10_070, 0) },
        { "HAZMAT_ENDERMAN", ("Voidgloom Seraph", 32_043, 0) },
        { "POCKET_ESPRESSO_MACHINE", ("Voidgloom Seraph", 128_172, 0) },
        { "SINFUL_DICE", ("Voidgloom Seraph", 108_453, 0) },
        { "ETHERWARP_MERGER", ("Voidgloom Seraph", 118_075, 0) },
        { "JUDGEMENT_CORE", ("Voidgloom Seraph", 885_562, 0) },
        { "EXCEEDINGLY_RARE_ENDER_ARTIFACT_UPGRADER", ("Voidgloom Seraph", 1_762_375, 0) },
        { "ENDSTONE_IDOL", ("Voidgloom Seraph", 3_542_250, 0) },
        { "UNFANGED_VAMPIRE_PART", ("Riftstalker Bloodfiend", 18_450, 0) },
        { "MCGRUBBER_BURGER", ("Riftstalker Bloodfiend", 18_450, 0) },
        { "OVERFLUX_CAPACITOR", ("Sven Packmaster", 1_232_700, 0) },
        { "GRIZZLY_BAIT", ("Sven Packmaster", 880_500, 0) },
        { "RED_CLAW_EGG", ("Sven Packmaster", 410_900, 0) },
        { "RUNE_COUTURE", ("Sven Packmaster", 219_833, 0) },
        { "HAMSTER_WHEEL", ("Sven Packmaster", 3_000, 0) },
        { "DYE_FLAME", ("Inferno Demonlord", 75_000_000, 0) },
        { "ARCHFIEND_DICE", ("Inferno Demonlord", 37_675, 0) },
        { "KELVIN_INVERTER", ("Inferno Demonlord", 14_270, 0) },
        { "SCORCHED_POWER_CRYSTAL", ("Inferno Demonlord", 12_558, 0) },
        { "MANA_DISINTEGRATOR", ("Inferno Demonlord", 10_192, 0) },
        { "HIGH_CLASS_ARCHFIEND_DICE", ("Inferno Demonlord", 194_939, 0) },
        
        // Catacombs items
        { "T7_BOOK", ("Catacombs F7", 208_410, 0) },
        { "FIRST_MASTER_STAR", ("Catacombs M2-M3", 12_000, 5_000_000) },
        { "SECOND_MASTER_STAR", ("Catacombs M4-M5", 20_000, 6_000_000) },
        { "THIRD_MASTER_STAR", ("Catacombs M4-M5", 48_900, 7_000_000) },
        { "FOURTH_MASTER_STAR", ("Catacombs M6-M7", 81_240, 8_000_000) },
        { "SHADOW_FURY", ("Catacombs F2-F5", 114_300, 15_000_000) },
        { "GIANTS_SWORD", ("Catacombs F6", 160_020, 25_000_000) },
        { "PRECURSOR_EYE", ("Catacombs F6", 266_699, 0) },
        { "NECRON_HANDLE", ("Catacombs M6-M7", 231_600, 100_000_000) },
        { "SCROLL_F7", ("Catacombs F7", 210_840, 50_000_000) },
        { "IMPLOSION_SCROLL", ("Catacombs M6-M7", 166_728, 50_000_000) },
        { "SHADOW_WARP_SCROLL", ("Catacombs M6-M7", 166_728, 50_000_000) },
        { "WITHER_SHIELD_SCROLL", ("Catacombs M6-M7", 166_728, 50_000_000) },
        { "DARK_CLAYMORE", ("Catacombs M6-M7", 416_820, 150_000_000) },
        { "QUICK_CLAW", ("Crystal Nucleus", 450_000, 0) },
        { "DIVANS_ALLOY", ("Crystal Nucleus", 1_000_000, 0) },
        { "DYE_NADESHIKO", ("Catacombs F7", 1_000_000, 0) },
        { "DYE_MATCHA", ("Revenant Horror", 75_000_000, 0) },
        { "DYE_CELESTE", ("Sven Packmaster", 75_000_000, 0) },
        { "DYE_BYZANTIUM", ("Voidgloom Seraph", 75_000_000, 0) },
        { "DYE_SANGRIA", ("Riftstalker Bloodfiend", 750_000, 0) },
        { "DYE_LIVID", ("Catacombs M4-M5", 1_000_000, 0) },
        { "DYE_NECRON", ("Catacombs M6-M7", 1_000_000, 0) },
        { "DYE_JADE", ("Crystal Nucleus", 5_000_000, 0) },
        { "MASTER_SKULL_TIER_5", ("Catacombs M6-M7", 173_675, 0) },
        { "STORM_THE_FISH", ("Catacombs M6-M7", 833_640, 0) },
        { "MAXOR_THE_FISH", ("Catacombs M6-M7", 833_640, 0) },
        { "GOLDOR_THE_FISH", ("Catacombs M6-M7", 833_640, 0) },
        { "PRECURSOR_GEAR", ("Catacombs M6-M7", 3_473, 0) },
        { "WITHER_CLOAK", ("Catacombs M6-M7", 8_683, 0) },
        { "WITHER_HELMET", ("Catacombs M6-M7", 8_683, 0) },
        { "WITHER_BLOOD", ("Catacombs M6-M7", 8_683, 0) },
        { "WITHER_BOOTS", ("Catacombs M6-M7", 8_683, 0) },
        { "WITHER_CATALYST", ("Catacombs M6-M7", 10_420, 0) },
        { "WITHER_LEGGINGS", ("Catacombs M6-M7", 13_025, 0) },
        { "AUTO_RECOMBOBULATOR", ("Catacombs M6-M7", 52_102, 0) },
        { "WITHER_CHESTPLATE", ("Catacombs M6-M7", 52_102, 0) },
        
        // Experimentation Table items
        { "TITANIC_EXPERIENCE_BOTTLE", ("Experimentation Table", 150_000, 0) },
        { "EXPERIMENT_THE_FISH", ("Experimentation Table", 500_000, 0) },
        { "METAPHYSICAL_SERUM", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_SCAVENGER_5", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_SHARPNESS_6", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_LIFE_STEAL_4", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_POWER_6", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_ENDER_SLAYER_6", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_THUNDERBOLT_6", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_GROWTH_6", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_CHANCE_4", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_BLAST_PROTECTION_6", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_RESPITE_3", ("Experimentation Table", 150_000, 0) },
        { "ENCHANTMENT_PROJECTILE_PROTECTION_6", ("Experimentation Table", 150_000, 0) },
        { "PESTHUNTING_GUIDE", ("Experimentation Table", 500_000, 0) },
        { "SEVERED_PINCER", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_CHANCE_5", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_THUNDERLORD_7", ("Catacombs M6-M7", 208_410, 0) },
        { "ENCHANTMENT_GIANT_KILLER_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_GRAVITY_6", ("Experimentation Table", 500_000, 0) },
        { "GOLDEN_BOUNTY", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_CRITICAL_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_SNIPE_4", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_LIFE_STEAL_5", ("Experimentation Table", 500_000, 0) },
        { "GOLD_BOTTLE_CAP", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_LOOTING_5", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_FIRST_STRIKE_5", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_FIRE_PROTECTION_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_CUBISM_6", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_TRIPLE_STRIKE_5", ("Experimentation Table", 500_000, 0) },
        { "CHAIN_END_TIMES", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_DRAIN_5", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_CLEAVE_6", ("Experimentation Table", 500_000, 0) },
        { "OCTOPUS_TENDRIL", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_TITAN_KILLER_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_LUCK_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_EXECUTE_6", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_POWER_7", ("Experimentation Table", 500_000, 0) },
        { "TROUBLED_BUBBLE", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_PROJECTILE_PROTECTION_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_GROWTH_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_SHARPNESS_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_VENOMOUS_6", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_PROTECTION_7", ("Experimentation Table", 500_000, 0) },
        { "ENCHANTMENT_PROSECUTE_6", ("Experimentation Table", 500_000, 0) },
        
        // Crystal Nucleus items
        { "DIVAN_ALLOY", ("Crystal Nucleus", 1_000_000, 0) },
        
        // Catacombs enchanted books
        { "ENCHANTMENT_REJUVENATE_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_REJUVENATE_2", ("Catacombs F2-F5", 40_000, 0) },
        { "ENCHANTMENT_REJUVENATE_3", ("Catacombs M6-M7", 4_168, 0) },
        { "ENCHANTMENT_ULTIMATE_LAST_STAND_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_ULTIMATE_LAST_STAND_2", ("Catacombs M6-M7", 4_168, 0) },
        { "ENCHANTMENT_ULTIMATE_WISE_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_ULTIMATE_WISE_2", ("Catacombs M6-M7", 5_210, 0) },
        { "ENCHANTMENT_BANK_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_BANK_2", ("Catacombs F2-F5", 40_000, 0) },
        { "ENCHANTMENT_BANK_3", ("Catacombs M6-M7", 8_336, 0) },
        { "ENCHANTMENT_WISDOM_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_WISDOM_2", ("Catacombs M6-M7", 8_336, 0) },
        { "ENCHANTMENT_COMBO_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_COMBO_2", ("Catacombs M6-M7", 4_168, 0) },
        { "ENCHANTMENT_NO_PAIN_NO_GAIN_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_NO_PAIN_NO_GAIN_2", ("Catacombs M6-M7", 10_420, 0) },
        { "ENCHANTMENT_ULTIMATE_JERRY_1", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_ULTIMATE_JERRY_2", ("Catacombs F2-F5", 40_000, 0) },
        { "ENCHANTMENT_ULTIMATE_JERRY_3", ("Catacombs M6-M7", 6_947, 0) },
        { "ENCHANTMENT_LEGION_1", ("Catacombs F2-F5", 40_000, 0) },
        { "ENCHANTMENT_LETHALITY_6", ("Catacombs F2-F5", 40_000, 0) },
        { "ENCHANTMENT_OVERLOAD_1", ("Catacombs F2-F5", 40_000, 0) },
        { "ENCHANTMENT_SWARM_1", ("Catacombs F6", 30_000, 0) },
        { "ENCHANTMENT_SOUL_EATER_1", ("Catacombs M6-M7", 5_210, 0) },
        { "ENCHANTMENT_ONE_FOR_ALL_1", ("Catacombs M6-M7", 52_102, 0) },
        { "ENCHANTMENT_FEATHER_FALLING_6", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_FEATHER_FALLING_7", ("Catacombs M6-M7", 13_025, 0) },
        { "ENCHANTMENT_INFINITE_QUIVER_6", ("Catacombs F1", 10_000, 0) },
        { "ENCHANTMENT_INFINITE_QUIVER_7", ("Catacombs M6-M7", 4_168, 0) },
        { "FIFTH_MASTER_STAR", ("Catacombs M6-M7", 138_940, 9_000_000) },
    };

    // Dynamically populated costs dictionary
    private static Dictionary<string, (double feathers, long unlockCost)> costs = PopulateCosts();

    private static Dictionary<string, (double feathers, long unlockCost)> PopulateCosts()
    {
        var result = new Dictionary<string, (double feathers, long unlockCost)>();

        foreach (var (itemTag, (meterType, expTarget, unlockCost)) in itemData)
        {
            if (progressPerFeather.TryGetValue(meterType, out var progressValue))
            {
                var feathersRequired = expTarget / progressValue;
                result[itemTag] = (feathersRequired, unlockCost);
            }
            else
            {
                Console.WriteLine($"Warning: Unknown meter type '{meterType}' for item '{itemTag}'");
            }
        }

        return result;
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Element item)
    {
        db.MsgLine($" {item.Name} {McColorCodes.GRAY}for {McColorCodes.AQUA}{socket.FormatPrice((item.SellPrice - item.Cost - item.UnlockCost) / item.FeathersRequired)} coins per feather",
                    "https://sky.coflnet.com/item/" + item.Tag,
                    $"Requires {McColorCodes.AQUA}{socket.FormatPrice(item.FeathersRequired)} feathers{McColorCodes.GRAY}, total cost: {McColorCodes.AQUA}{socket.FormatPrice(item.Cost + item.UnlockCost)} coins\n"
                    + (item.UnlockCost > 0 ? $"Unlock cost: {McColorCodes.AQUA}{socket.FormatPrice(item.UnlockCost)} coins{McColorCodes.GRAY}(included in total)\n" : "")
                    + $"Profit buying feathers at ah: {McColorCodes.AQUA}{socket.FormatPrice(item.SellPrice - item.Cost - item.UnlockCost)} coins\n"
                    + $"{McColorCodes.GRAY}Estimated sell value: {McColorCodes.AQUA}{socket.FormatPrice(item.SellPrice)} coins\n"
                    + "Click to check history on website");
    }

    protected override async Task<IEnumerable<Element>> GetElements(MinecraftSocket socket, string val)
    {
        var namesTask = socket.GetService<Items.Client.Api.IItemsApi>().ItemNamesGetAsync();
        var bazaarTask = socket.GetService<IBazaarApi>().GetAllPricesAsync();
        var cleanPrices = await socket.GetService<ISniperClient>().GetCleanPrices();
        var names = (await namesTask)?.ToDictionary(i => i.Tag, i => i.Name) ?? [];
        foreach (var listing in await bazaarTask)
        {
            cleanPrices[listing.ProductId] = (long)listing.BuyPrice;
        }
        var itemService = socket.GetService<HypixelItemService>();
        var all = new List<Element>();
        var featherPrice = cleanPrices.GetValueOrDefault("ANANKE_FEATHER");
        foreach (var item in costs)
        {
            if (!cleanPrices.TryGetValue(item.Key, out var price))
            {
                Console.WriteLine($"Price for {item.Key} not found in clean prices, skipping.");
                continue;
            }
            var (feathersRequired, unlockCost) = item.Value;
            var costOfFeathers = (long)(featherPrice * (feathersRequired + 0.01));
            all.Add(new Element
            {
                Name = names.GetValueOrDefault(item.Key, item.Key),
                Tag = item.Key,
                Cost = costOfFeathers,
                FeathersRequired = feathersRequired,
                SellPrice = price,
                UnlockCost = unlockCost
            });
        }
        return all.OrderByDescending(e => (e.SellPrice - e.Cost - e.UnlockCost));
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<Element> elements, IEnumerable<Element> toDisplay)
    {
        db.MsgLine($"Assuming you can buy all required feathers at {McColorCodes.AQUA}{socket.FormatPrice(toDisplay.Average(e => e.Cost / e.FeathersRequired))} coins");
    }

    protected override string GetId(Element elem)
    {
        return elem.Tag;
    }

    public class AnankeCost
    {
        public string Tag { get; set; }
        public double FeathersRequired { get; set; }
        public long UnlockCost { get; set; }
    }
    public class Element
    {
        public string Tag { get; set; }
        public long SellPrice { get; set; }
        public long Cost { get; set; }
        public double FeathersRequired { get; set; }
        public string AuctionUuid { get; set; }
        public long UnlockCost { get; set; }
        public string Name { get; internal set; }
    }
}
