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
    // Updated: Added unlock costs (in coins) where known, 0 if unknown
    // RNG Meter feather costs based on progression rates (1 feather = 100,000 XP)
    private static Dictionary<string, (double feathers, long unlockCost)> costs = new()
    {
        // Slayer RNG Meter items
        { "SCYTHE_BLADE", (0.98, 0) }, // Revenant Horror: 500k XP = 5.00 feathers
        { "SHARD_OF_THE_SHREDDED", (1.84, 0) },
        { "WARDEN_HEART", (7.40, 0) },
        { "SEVERED_HAND", (5.0, 0) },
        { "REVENANT_CATALYST", (0.49, 0) },
        { "TARANTULA_SILK", (0.035, 0) }, // Tarantula Broodfather: 500k XP = 5.00 feathers
        { "TARANTULA_CATALYST", (1.17, 0) },
        { "FLY_SWATTER", (2.34, 0) },
        { "VIAL_OF_VENOM", (3.51, 0) },
        { "PRIMORDIAL_EYE", (35.13, 0) },
        { "ENSNARED_SNAIL", (5.0, 0) },
        { "DIGESTED_MOSQUITO", (7.03, 0) },
        { "TARANTULA_TALISMAN", (2.34, 0) },
        { "SHRIVELED_WASP", (3.51, 0) },
        { "SUMMONING_EYE", (0.74, 0) }, // Voidgloom Seraph: 70k XP = 0.70 feathers
        { "TRANSMISSION_TUNER", (0.22, 0) },
        { "NULL_ATOM", (0.10, 0) },
        { "HAZMAT_ENDERMAN", (0.32, 0) },
        { "POCKET_ESPRESSO_MACHINE", (1.29, 0) },
        { "SINFUL_DICE", (1.09, 0) },
        { "ETHERWARP_MERGER", (1.18, 0) },
        { "JUDGEMENT_CORE", (8.86, 0) },
        { "EXCEEDINGLY_RARE_ENDER_ARTIFACT_UPGRADER", (17.71, 0) },
        { "ENDSTONE_IDOL", (35.42, 0) },
        { "UNFANGED_VAMPIRE_PART", (0.18, 0) }, // Riftstalker Bloodfiend: 8k XP = 0.08 feathers
        { "MCGRUBBER_BURGER", (0.18, 0) },
        { "OVERFLUX_CAPACITOR", (12.32, 0) }, // Sven Packmaster: 300k XP = 3.00 feathers
        { "GRIZZLY_BAIT", (8.81, 0) },
        { "RED_CLAW_EGG", (4.11, 0) },
        { "RUNE_COUTURE", (2.20, 0) },
        { "HAMSTER_WHEEL", (0.03, 0) },
        // Inferno Demonlord: 85k XP = 0.85 feathers
        { "DYE_FLAME", (0.85, 0) },
        { "ARCHFIEND_DICE", (0.38, 0) },
        { "KELVIN_INVERTER", (0.14, 0) },
        { "SCORCHED_POWER_CRYSTAL", (0.13, 0) },
        { "MANA_DISINTEGRATOR", (0.10, 0) },
        { "HIGH_CLASS_ARCHFIEND_DICE", (1.95, 0) },
        // Catacombs RNG Meters
        { "T7_BOOK", (5.00, 0) },
        { "FIRST_MASTER_STAR", (0.98, 5_000_000) }, // M3: 12k XP = 0.12 feathers
        { "SECOND_MASTER_STAR", (0.97, 6_000_000) }, // M4: 20k XP = 0.20 feathers
        { "THIRD_MASTER_STAR", (2.45, 7_000_000) }, // F5: 40k XP = 0.40 feathers
        { "FOURTH_MASTER_STAR", (4.06, 8_000_000) }, // F6: 30k XP = 0.30 feathers
        { "FIFTH_MASTER_STAR", (20.29, 9_000_000) }, // F7/M7: 8k XP = 0.08 feathers
        { "SHADOW_FURY", (2.86, 15_000_000) }, // F5: 40k XP = 0.40 feathers
        { "GIANTS_SWORD", (5.33, 25_000_000) }, // F6: 30k XP = 0.30 feathers
        { "PRECURSOR_EYE", (8.89, 0) },
        { "NECRON_HANDLE", (33.79, 100_000_000) }, // F7/M7: 8k XP = 0.08 feathers
        { "SCROLL_F7", (26.38, 50_000_000) },
        { "IMPLOSION_SCROLL", (24.33, 50_000_000) },
        { "SHADOW_WARP_SCROLL", (24.33, 50_000_000) },
        { "WITHER_SHIELD_SCROLL", (24.33, 50_000_000) },
        { "DARK_CLAYMORE", (60.83, 150_000_000) },
        { "QUICK_CLAW", (26.47, 0) },
        { "DIVANS_ALLOY", (58.82, 0) },
        { "DYE_NADESHIKO", (25, 0) },
        { "DYE_MATCHA", (150, 0) },
        { "DYE_CELESTE", (250, 0) },
        { "DYE_BYZANTIUM", (1072, 0) },
        { "DYE_SANGRIA", (94, 0) },
        { "DYE_LIVID", (50, 0) },
        { "DYE_NECRON", (143, 0) },
        { "DYE_JADE", (295, 0) },
        // Experimentation Table: 100k XP = 1.00 feather
        { "TITANIC_EXPERIENCE_BOTTLE", (0.15, 0) },
        { "EXPERIMENT_THE_FISH", (5.00, 0) },
        { "METAPHYSICAL_SERUM", (0.5, 0) },
        { "ENCHANTMENT_SCAVENGER_5", (1.5, 0) },
        { "ENCHANTMENT_SHARPNESS_6", (1.5, 0) },
        { "ENCHANTMENT_LIFE_STEAL_4", (1.5, 0) },
        { "ENCHANTMENT_POWER_6", (1.5, 0) },
        { "ENCHANTMENT_ENDER_SLAYER_6", (1.5, 0) },
        { "ENCHANTMENT_THUNDERBOLT_6", (1.5, 0) },
        { "ENCHANTMENT_GROWTH_6", (1.5, 0) },
        { "ENCHANTMENT_CHANCE_4", (1.5, 0) },
        { "ENCHANTMENT_BLAST_PROTECTION_6", (1.5, 0) },
        { "ENCHANTMENT_RESPITE_3", (1.5, 0) },
        { "ENCHANTMENT_PROJECTILE_PROTECTION_6", (1.5, 0) },
        { "PESTHUNTING_GUIDE", (5.0, 0) },
        { "SEVERED_PINCER", (5.0, 0) },
        { "ENCHANTMENT_CHANCE_5", (5.0, 0) },
        { "ENCHANTMENT_THUNDERLORD_7", (5.0, 0) },
        { "ENCHANTMENT_GIANT_KILLER_7", (5.0, 0) },
        { "ENCHANTMENT_GRAVITY_6", (5.0, 0) },
        { "GOLDEN_BOUNTY", (5.0, 0) },
        { "ENCHANTMENT_CRITICAL_7", (5.0, 0) },
        { "ENCHANTMENT_SNIPE_4", (5.0, 0) },
        { "ENCHANTMENT_LIFE_STEAL_5", (5.0, 0) },
        { "GOLD_BOTTLE_CAP", (5.0, 0) },
        { "ENCHANTMENT_LOOTING_5", (5.0, 0) },
        { "ENCHANTMENT_FIRST_STRIKE_5", (5.0, 0) },
        { "ENCHANTMENT_FIRE_PROTECTION_7", (5.0, 0) },
        { "ENCHANTMENT_CUBISM_6", (5.0, 0) },
        { "ENCHANTMENT_TRIPLE_STRIKE_5", (5.0, 0) },
        { "CHAIN_END_TIMES", (5.0, 0) },
        { "ENCHANTMENT_DRAIN_5", (5.0, 0) },
        { "ENCHANTMENT_CLEAVE_6", (5.0, 0) },
        { "OCTOPUS_TENDRIL", (5.0, 0) },
        { "ENCHANTMENT_TITAN_KILLER_7", (5.0, 0) },
        { "ENCHANTMENT_LUCK_7", (5.0, 0) },
        { "ENCHANTMENT_EXECUTE_6", (5.0, 0) },
        { "ENCHANTMENT_POWER_7", (5.0, 0) },
        { "TROUBLED_BUBBLE", (5.0, 0) },
        { "ENCHANTMENT_PROJECTILE_PROTECTION_7", (5.0, 0) },
        { "ENCHANTMENT_GROWTH_7", (5.0, 0) },
        { "ENCHANTMENT_SHARPNESS_7", (5.0, 0) },
        { "ENCHANTMENT_VENOMOUS_6", (5.0, 0) },
        { "ENCHANTMENT_PROTECTION_7", (5.0, 0) },
        { "ENCHANTMENT_PROSECUTE_6", (5.0, 0) },
        // Crystal Nucleus: 17k XP = 0.17 feathers
        { "QUICK_CLAW", (0.17, 0) },
        { "DIVAN_ALLOY", (1.00, 0) },
        // Catacombs Enchanted Books from various floors
        { "ENCHANTMENT_REJUVENATE_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_REJUVENATE_2", (0.20, 0) }, // F2-F5: 40k XP average
        { "ENCHANTMENT_REJUVENATE_3", (0.40, 0) }, // F5-F6: 40-30k XP
        { "ENCHANTMENT_ULTIMATE_LAST_STAND_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_ULTIMATE_LAST_STAND_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_ULTIMATE_WISE_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_ULTIMATE_WISE_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_BANK_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_BANK_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_BANK_3", (0.30, 0) }, // F5-F6: 30k XP
        { "ENCHANTMENT_WISDOM_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_WISDOM_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_COMBO_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_COMBO_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_NO_PAIN_NO_GAIN_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_NO_PAIN_NO_GAIN_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_ULTIMATE_JERRY_1", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_ULTIMATE_JERRY_2", (0.20, 0) }, // F3-F5: 40k XP average
        { "ENCHANTMENT_ULTIMATE_JERRY_3", (0.30, 0) }, // F5-F6: 30k XP
        { "ENCHANTMENT_LEGION_1", (0.20, 0) }, // F5: 40k XP
        { "ENCHANTMENT_LETHALITY_6", (0.20, 0) }, // F5: 40k XP
        { "ENCHANTMENT_OVERLOAD_1", (0.20, 0) }, // F5: 40k XP
        { "ENCHANTMENT_SWARM_1", (0.30, 0) }, // F6: 30k XP
        { "ENCHANTMENT_SOUL_EATER_1", (0.08, 0) }, // F7: 8k XP
        { "ENCHANTMENT_ONE_FOR_ALL_1", (0.08, 0) }, // F7: 8k XP
        { "ENCHANTMENT_FEATHER_FALLING_6", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_FEATHER_FALLING_7", (0.08, 0) }, // F7: 8k XP
        { "ENCHANTMENT_INFINITE_QUIVER_6", (0.10, 0) }, // F1: 10k XP
        { "ENCHANTMENT_INFINITE_QUIVER_7", (0.08, 0) }, // F7: 8k XP
        { "ENCHANTMENT_THUNDERLORD_7", (0.08, 0) }, // F7: 8k XP
    };

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Element item)
    {
        db.MsgLine($" {item.Name} {McColorCodes.GRAY}for {McColorCodes.AQUA}{socket.FormatPrice((item.Price - item.Cost - item.UnlockCost) / item.FeathersRequired)} coins per feather",
                    "https://sky.coflnet.com/item/" + item.Tag,
                    $"Requires {McColorCodes.AQUA}{item.FeathersRequired} feathers{McColorCodes.GRAY}, total cost: {McColorCodes.AQUA}{socket.FormatPrice(item.Cost + item.UnlockCost)} coins\n"
                    + (item.UnlockCost > 0 ? $"Unlock cost: {McColorCodes.AQUA}{socket.FormatPrice(item.UnlockCost)} coins{McColorCodes.GRAY}(included in total)\n" : "")
                    + $"Estimated profit buying at ah: {McColorCodes.AQUA}{socket.FormatPrice(item.Price - item.Cost - item.UnlockCost)} coins\n"
                    + $"{McColorCodes.GRAY}Estimated sell value: {McColorCodes.AQUA}{socket.FormatPrice(item.Price)} coins\n"
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
            var costOfFeathers = featherPrice * ((int)(feathersRequired + 0.99));
            all.Add(new Element
            {
                Name = names.GetValueOrDefault(item.Key, item.Key),
                Tag = item.Key,
                Cost = costOfFeathers,
                FeathersRequired = feathersRequired,
                Price = price,
                UnlockCost = unlockCost
            });
        }
        return all.OrderByDescending(e => (e.Price - e.Cost - e.UnlockCost));
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
        public long Price { get; set; }
        public long Cost { get; set; }
        public double FeathersRequired { get; set; }
        public string AuctionUuid { get; set; }
        public long UnlockCost { get; set; }
        public string Name { get; internal set; }
    }
}
