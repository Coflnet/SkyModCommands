using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Core.Tracing;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Shows you the cheapest attributes to upgrade or unlock")]
public class CheapAttribCommand : ReadOnlyListCommand<CheapAttribCommand.CheapAttribute>
{
    protected override string Title => "Cheapest Attributes";
    protected override async Task<IEnumerable<CheapAttribute>> GetElements(MinecraftSocket socket, string val)
    {
        var extractedTask = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var bazaarPrices = (await socket.GetService<IBazaarApi>().GetAllPricesAsync()).Where(i => i.ProductId.StartsWith("SHARD_"));
        var buyPrice = bazaarPrices.ToDictionary(i => i.ProductId, i => i.BuyPrice);
        var SellPrice = bazaarPrices.ToDictionary(i => i.ProductId, i => i.SellPrice);

        var extractedInfo = (await extractedTask)?.AttributeLevel ?? new();
        var notUnlocked = SellPrice.Where(i => !extractedInfo.ContainsKey(ShardNameToAttributeName.GetValueOrDefault(i.Key, "nope")) && i.Value < 20_000_000_000).OrderBy(i => i.Value).ToList();
        var unlocked = SellPrice.Where(i => extractedInfo.TryGetValue(ShardNameToAttributeName.GetValueOrDefault(i.Key, "nope"), out var level) && level < 10 && i.Value < 20_000_000_000).OrderBy(i => i.Value).ToList();

        foreach (var item in bazaarPrices)
        {
            if (!ShardNameToAttributeName.ContainsKey(item.ProductId))
                Console.WriteLine($"Missing shard name for {item.ProductId}, please add it to ShardNameToAttributeName in CheapAttribCommand.cs");
        }

        Activity.Current.Log($"Found {notUnlocked.Count} not unlocked and {unlocked.Count} unlocked attributes");
        Activity.Current.Log(JsonConvert.SerializeObject(extractedInfo));

        return notUnlocked.Select(i => new CheapAttribute
        {
            Name = ShardNameToAttributeName.GetValueOrDefault(i.Key, i.Key),
            Tag = i.Key,
            Price = i.Value,
            Type = "unlock"
        }).Concat(unlocked.Select(i => new CheapAttribute
        {
            Name = ShardNameToAttributeName.GetValueOrDefault(i.Key, i.Key),
            Tag = i.Key,
            Price = i.Value,
            Type = "upgrade"
        })).Where(i => i.Price > 0).ToList();
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, CheapAttribute elem)
    {
        db.MsgLine($"{McColorCodes.YELLOW}{elem.Name} {McColorCodes.GOLD}{socket.FormatPrice(elem.Price)}",
            "/bz " + elem.Tag.Replace("SHARD_", ""),
            $"Click to open bazaar for {McColorCodes.AQUA}{elem.Name}"
          + $"\n{McColorCodes.GRAY}Shard: {McColorCodes.YELLOW}{elem.Tag.Replace("SHARD_", "")}\n{McColorCodes.GRAY}Type: {McColorCodes.YELLOW}{elem.Type}");
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<CheapAttribute> elements, IEnumerable<CheapAttribute> toDisplay)
    {
        db.MsgLine("Cost per one shard is displayed")
            .CoflCommandButton<CheapAttribCommand>(McColorCodes.YELLOW + "only unlock ", "unlock", "Filter for unlocking shards")
            .CoflCommandButton<CheapAttribCommand>(McColorCodes.GREEN + "only upgrade ", "upgrade", "Filter for upgrading shards")
            .If(() => elements.Count(e => e.Type == "unlock") >= 174, db => db.MsgLine($"You have many attributes to unlock, make sure to open your attribute menu so we can see which you already have and make sure to only show ones you are missing"));
    }

    protected override string GetId(CheapAttribute elem)
    {
        return elem.Tag + elem.Type;
    }
    public class CheapAttribute
    {
        public string Name { get; set; }
        public string Tag { get; set; }
        public double Price { get; set; }
        public string Type { get; set; }
    }

    public Dictionary<string, string> ShardNameToAttributeName = new()
        {
            { "SHARD_GROVE", "Nature Elemental"
            },
            { "SHARD_MIST", "Fog Elemental"
            },
            { "SHARD_FLASH", "Light Elemental"
            },
            { "SHARD_PHANPYRE", "Nocturnal Animal"
            },
            { "SHARD_COD", "Cheapstake"
            },
            { "SHARD_PHANFLARE", "Moonglade Mastery"
            },
            { "SHARD_NIGHT_SQUID", "Fisherman"
            },
            { "SHARD_LAPIS_ZOMBIE", "Experience"
            },
            { "SHARD_HIDEONLEAF", "Mossy Box"
            },
            { "SHARD_VERDANT", "Forest Fishing"
            },
            { "SHARD_CHILL", "Skeletal Ruler"
            },
            { "SHARD_SEA_ARCHER", "Monster Bait"
            },
            { "SHARD_VORACIOUS_SPIDER", "Arthropod Resistance"
            },
            { "SHARD_HIDEONGIFT", "Happy Box"
            },
            { "SHARD_BIRRIES", "Yummy"
            },
            { "SHARD_TANK_ZOMBIE", "Undead Resistance"
            },
            { "SHARD_CROW", "Fig Sharpening"
            },
            { "SHARD_TADGANG", "Unity is Strength"
            },
            { "SHARD_ZEALOT", "Ender Resistance"
            },
            { "SHARD_CORALOT", "Bucket Lover"
            },
            { "SHARD_HARPY", "Tree Lurker"
            },
            { "SHARD_MUDWORM", "Visitor Bait"
            },
            { "SHARD_GOLDEN_GHOUL", "Midas Touch"
            },
            { "SHARD_AZURE", "Forest Strength"
            },
            { "SHARD_BEZAL", "Blazing Resistance"
            },
            { "SHARD_YOG", "Yog Membrane"
            },
            { "SHARD_BOREAL_OWL", "Owl Friend"
            },
            { "SHARD_NEWT", "Decent Karma"
            },
            { "SHARD_MINER_ZOMBIE", "Rotten Pickaxe"
            },
            { "SHARD_BRAMBLE", "Wood Elemental"
            },
            { "SHARD_TIDE", "Water Elemental"
            },
            { "SHARD_QUAKE", "Stone Elemental"
            },
            { "SHARD_SPARROW", "Fig Collector"
            },
            { "SHARD_GOLDFIN", "Gold Bait"
            },
            { "SHARD_TROGLOBYTE", "Mountain Climber"
            },
            { "SHARD_HIDEONCAVE", "Bigger Box"
            },
            { "SHARD_SALAMANDER", "Good Karma"
            },
            { "SHARD_CUBOA", "Echo of boxes"
            },
            { "SHARD_PEST", "Pest Luck"
            },
            { "SHARD_MOSSYBIT", "Forest Trap"
            },
            { "SHARD_RAIN_SLIME", "Mana Steal"
            },
            { "SHARD_SEER", "Dragon Shortbow Improvement"
            },
            { "SHARD_HERON", "Mangrove Sharpening"
            },
            { "SHARD_OBSIDIAN_DEFENDER", "Speed"
            },
            { "SHARD_SALMON", "Lost and Found"
            },
            { "SHARD_VIPER", "Hunter's Fang"
            },
            { "SHARD_PRAYING_MANTIS", "Insect Power"
            },
            { "SHARD_ZOMBIE_SOLDIER", "Undead Ruler"
            },
            { "SHARD_BAMBULEAF", "Strong Arms"
            },
            { "SHARD_SYCOPHANT", "Life Recovery"
            },
            { "SHARD_SEAGULL", "Mangrove Collector"
            },
            { "SHARD_ENT", "Spirit Axe"
            },
            { "SHARD_SOUL_OF_THE_ALPHA", "Combo"
            },
            { "SHARD_MOCHIBEAR", "Strong Legs"
            },
            { "SHARD_MAGMA_SLUG", "Infection"
            },
            { "SHARD_FLAMING_SPIDER", "Arthropod Ruler"
            },
            { "SHARD_KIWI", "Kat's Favorite"
            },
            { "SHARD_BRUISER", "Ender Ruler"
            },
            { "SHARD_STRIDER_SURFER", "Magmatic Ruler"
            },
            { "SHARD_RANA", "Battle Frog"
            },
            { "SHARD_TERMITE", "Infiltration"
            },
            { "SHARD_SYLVAN", "Forest Elemental"
            },
            { "SHARD_CASCADE", "Torrent Elemental"
            },
            { "SHARD_BOLT", "Lightning Elemental"
            },
            { "SHARD_BAMBLOOM", "Animal Expertise"
            },
            { "SHARD_TOAD", "Frog Legs"
            },
            { "SHARD_GLACITE_WALKER", "Essence of Ice"
            },
            { "SHARD_BEACONMITE", "Beacon Zealot"
            },
            { "SHARD_LIZARD_KING", "Great Karma"
            },
            { "SHARD_PYTHON", "Hunter's Pressure"
            },
            { "SHARD_INVISIBUG", "Fancy Visit"
            },
            { "SHARD_PIRANHA", "Moonglade Serendipity"
            },
            { "SHARD_HIDEONGEON", "Catacombs Box"
            },
            { "SHARD_LAPIS_SKELETON", "Bone Font"
            },
            { "SHARD_CROPEETLE", "Crop Bug"
            },
            { "SHARD_DROWNED", "Humanoid Ruler"
            },
            { "SHARD_STAR_SENTRY", "Atomized Mithril"
            },
            { "SHARD_HIDEONDRA", "Kuudra's Box"
            },
            { "SHARD_ABYSSAL_LANTERN", "Dwarven Serendipity"
            },
            { "SHARD_ARACHNE", "Essence of Arthropods"
            },
            { "SHARD_BITBUG", "Cookie Eater"
            },
            { "SHARD_REVENANT", "Essence of Unliving"
            },
            { "SHARD_SILENTDEPTH", "Crystal Serendipity"
            },
            { "SHARD_SKELETOR", "Deadeye"
            },
            { "SHARD_THYST", "Atomized Crystals"
            },
            { "SHARD_QUARTZFANG", "Quartz Speed"
            },
            { "SHARD_HIDEONRING", "Accessory Size"
            },
            { "SHARD_SNOWFIN", "Winter's Serendipity"
            },
            { "SHARD_KADA_KNIGHT", "Lifeline"
            },
            { "SHARD_CARROT_KING", "Rabbit Crew"
            },
            { "SHARD_WITHER_SPECTER", "Breeze"
            },
            { "SHARD_MATCHO", "Ignition"
            },
            { "SHARD_LADYBUG", "Pretty Clothes"
            },
            { "SHARD_LUMISQUID", "Extreme Pressure"
            },
            { "SHARD_CROCODILE", "Pure Reptile"
            },
            { "SHARD_BULLFROG", "Berry Enjoyer"
            },
            { "SHARD_DREADWING", "Essence of the Forest"
            },
            { "SHARD_JOYDIVE", "Deep Diving"
            },
            { "SHARD_STALAGMIGHT", "Atomized Glacite"
            },
            { "SHARD_FUNGLOOM", "Fungy Luck"
            },
            { "SHARD_EEL", "Eelastic"
            },
            { "SHARD_KING_COBRA", "Hunter's Grasp"
            },
            { "SHARD_LAVA_FLAME", "Blazing Fortune"
            },
            { "SHARD_DRACONIC", "Essence of Dragons"
            },
            { "SHARD_FALCON", "Battle Experience"
            },
            { "SHARD_INFERNO_KOI", "Crimson Serendipity"
            },
            { "SHARD_WITHER", "Essence of Wither"
            },
            { "SHARD_GECKO", "Echo of Sharpening"
            },
            { "SHARD_TERRA", "Earth Elemental"
            },
            { "SHARD_CRYO", "Frost Elemental"
            },
            { "SHARD_AERO", "Wind Elemental"
            },
            { "SHARD_PANDARAI", "Foraging Wisdom"
            },
            { "SHARD_LEVIATHAN", "Excellent Karma"
            },
            { "SHARD_ALLIGATOR", "Echo of Resistance"
            },
            { "SHARD_FENLORD", "Berry Mogul"
            },
            { "SHARD_BASILISK", "Hunter's Suppress"
            },
            { "SHARD_IGUANA", "Echo of Atomized"
            },
            { "SHARD_MORAY_EEL", "Lucky Rod"
            },
            { "SHARD_THORN", "Dominance"
            },
            { "SHARD_LUNAR_MOTH", "Lunar Power"
            },
            { "SHARD_FIRE_EEL", "Trophy Hunter"
            },
            { "SHARD_BAL", "Deep Technique"
            },
            { "SHARD_HIDEONSACK", "Sack Size"
            },
            { "SHARD_WATER_HYDRA", "Fishing Speed"
            },
            { "SHARD_FLARE", "Blazing"
            },
            { "SHARD_SEA_EMPEROR", "Sea Wisdom"
            },
            { "SHARD_PRINCE", "Reborn"
            },
            { "SHARD_KOMODO_DRAGON", "Echo of Essence"
            },
            { "SHARD_MIMIC", "Faker"
            },
            { "SHARD_SHELLWISE", "Shell"
            },
            { "SHARD_BARBARIAN_DUKE_X", "Warrior"
            },
            { "SHARD_TOUCAN", "Why Not More"
            },
            { "SHARD_HELLWISP", "Matriarch Cubs"
            },
            { "SHARD_CAIMAN", "Echo of Ruler"
            },
            { "SHARD_FIREFLY", "Solar Power"
            },
            { "SHARD_SEA_SERPENT", "Echo of Hunter"
            },
            { "SHARD_GHOST", "Veil"
            },
            { "SHARD_XYZ", "Mana Regeneration"
            },
            { "SHARD_LEATHERBACK", "Hunt Wisdom"
            },
            { "SHARD_CAVERNSHADE", "Cavern Wisdom"
            },
            { "SHARD_DRAGONFLY", "Garden Wisdom"
            },
            { "SHARD_TENEBRIS", "Shadow Elemental"
            },
            { "SHARD_BLIZZARD", "Snow Elemental"
            },
            { "SHARD_TEMPEST", "Storm Elemental"
            },
            { "SHARD_CHAMELEON", "Reptiloid"
            },
            { "SHARD_TIAMAT", "Echo of Echoes"
            },
            { "SHARD_WYVERN", "Echo of Wisdom"
            },
            { "SHARD_TORTOISE", "Cloak Improvement"
            },
            { "SHARD_ENDSTONE_PROTECTOR", "Paramount Fortitude"
            },
            { "SHARD_NAGA", "Charmed"
            },
            { "SHARD_LAPIS_CREEPER", "Book Wisdom"
            },
            { "SHARD_WARTYBUG", "Wart Eater"
            },
            { "SHARD_SPIKE", "Payback"
            },
            { "SHARD_KRAKEN", "Vitality"
            },
            { "SHARD_TAURUS", "Maximal Torment"
            },
            { "SHARD_DAEMON", "Pity"
            },
            { "SHARD_MOLTENFISH", "Star Bait"
            },
            { "SHARD_SHINYFISH", "Help From above"
            },
            { "SHARD_ANANKE", "Wings of Destiny"
            },
            { "SHARD_HIDEONBOX", "Tuning Box"
            },
            { "SHARD_LORD_JAWBUS", "Crimson Hook"
            },
            { "SHARD_BURNINGSOUL", "Attack Speed"
            },
            { "SHARD_CINDER_BAT", "Magic Find"
            },
            { "SHARD_MEGALITH", "Hunter's Karma"
            },
            { "SHARD_POWER_DRAGON", "Elite"
            },
            { "SHARD_CONDOR", "Pet Wisdom"
            },
            { "SHARD_SUN_FISH", "Starborn"
            },
            { "SHARD_APEX_DRAGON", "Veteran"
            },
            { "SHARD_DODO", "Rare Bird"
            },
            { "SHARD_JORMUNG", "Unlimited Power"
            },
            { "SHARD_ETHERDRAKE", "Unlimited Energy"
            },
            { "SHARD_GALAXY_FISH", "Ultimate DNA"
            },
            { "SHARD_MOLTHORN", "Almighty"
            },
            { "SHARD_STARBORN", "Echo of Elemental"
            },
            {"SHARD_HUMMINGBIRD", "Chop"},
            {"SHARD_TITANOBOA", "Bayou Biter"},
        };


}

public static class CommonDialogExtension
{
    public static async Task<bool> ReguirePremPlus(this IMinecraftSocket socket)
    {
        if (await socket.UserAccountTier() >= Shared.AccountTier.PREMIUM_PLUS)
        {
            return true;
        }
        socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
            $"{McColorCodes.RED}{McColorCodes.BOLD}ABORTED\n"
            + $"{McColorCodes.RED}You need to be a premium plus user to use this command"
            + $"{McColorCodes.YELLOW}\n[Click to purchase prem+]",
            "premium_plus", $"Click to purchase prem+"));
        return false;
    }
}
