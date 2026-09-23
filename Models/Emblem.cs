using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Models;

/// <summary>
/// A displayable emblem (achievement badge). "Emblem" (the symbol/name shown in chat) is purely a mod
/// presentation concern; eligibility is backed by an achievement or derived from other eligibility rules.
/// </summary>
public class Emblem
{
    /// <summary>
    /// For achievement-backed emblems, the achievement this emblem represents. Its value MUST match the
    /// name of a member of the authoritative <c>Achievement</c> enum in SkyUserState (generated into this project as
    /// <c>Coflnet.Sky.PlayerState.Client.Model.Achievement</c>). Once that client is regenerated with the
    /// achievement enum, prefer <c>Achievement.X.ToString()</c> here so a removed/renamed achievement
    /// breaks the build instead of silently drifting.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// The color coded unicode symbol shown in front of chat messages. Kept to glyphs the
    /// Minecraft default (unifont) fallback renders - no emoji, which show as missing boxes.
    /// </summary>
    public string Symbol { get; }
    /// <summary>
    /// Short human readable name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// How the emblem is unlocked / what it stands for.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// The acquisition topic used to group emblems in the selection menu.
    /// </summary>
    public string Category { get; }
    /// <summary>
    /// When true and the emblem is still locked it is shown as "???" - the unlock condition is a surprise.
    /// </summary>
    public bool Mysterious { get; }

    public Emblem(string id, string symbol, string name, string description, string category, bool mysterious = false)
    {
        Id = id;
        Symbol = symbol;
        Name = name;
        Description = description;
        Category = category;
        Mysterious = mysterious;
    }
}

/// <summary>
/// The catalog of all emblems. The id constants of the achievement backed emblems mirror the authoritative
/// <c>Achievement</c> enum names from SkyUserState - keep them in sync (see <see cref="Emblem.Id"/>).
/// The account-age, purchase-based, and moderator emblems are exceptions: they are not achievements at all.
/// Whether a player has one is derived on read from the account creation date, purchase history, or moderator
/// role in <see cref="Services.EmblemService.GetUnlockedForSocket"/>, so their ids don't appear in that enum.
/// </summary>
public static class Emblems
{
    public const string AchievementCategory = "Achievements";
    public const string ModeratorCategory = "Moderator";
    public const string PremiumCategory = "Premium Time";
    public const string PremiumPlusCategory = "Premium+ Time";
    public const string PreApiCategory = "Pre API Purchases";
    public const string AccountAgeCategory = "Account Age";
    public const string MysteryCategory = "Mysteries";

    public const string FirstLowball = "FirstLowball";
    public const string BazaarFlipProfit = "BazaarFlipProfit";
    public const string BazaarFlipLoss = "BazaarFlipLoss";
    public const string Whale = "Whale";
    public const string NightOwl = "NightOwl";
    public const string DiamondHands = "DiamondHands";
    // role emblem - derived from moderator status, not backed by an Achievement
    public const string Moderator = "Moderator";
    public static readonly string ModeratorSymbol = McColorCodes.GOLD + "ⓂⓄⒹ";
    // account-age emblems - derived from the account creation date, not backed by an Achievement
    public const string OneYearVeteran = "OneYearVeteran";
    public const string ThreeYearVeteran = "ThreeYearVeteran";
    public const string FiveYearVeteran = "FiveYearVeteran";
    // premium-time emblems - derived from accumulated purchased Premium/Premium+ time
    public const string PremiumSixMonths = "PremiumSixMonths";
    public const string PremiumOneYear = "PremiumOneYear";
    public const string PremiumTwoYears = "PremiumTwoYears";
    public const string PremiumThreeYears = "PremiumThreeYears";
    public const string PremiumPlusSixMonths = "PremiumPlusSixMonths";
    public const string PremiumPlusOneYear = "PremiumPlusOneYear";
    public const string PremiumPlusTwoYears = "PremiumPlusTwoYears";
    public const string PremiumPlusThreeYears = "PremiumPlusThreeYears";
    // Pre API supporter emblems - derived from the number of Pre API purchases
    public const string PreApiOnePurchase = "PreApiOnePurchase";
    public const string PreApiFivePurchases = "PreApiFivePurchases";
    public const string PreApiTenPurchases = "PreApiTenPurchases";
    public const string PreApiTwentyPurchases = "PreApiTwentyPurchases";

    /// <summary>
    /// All emblems in display order. Colors are baked into the symbol so it keeps its color when
    /// prepended in front of the rank color in chat.
    /// </summary>
    public static readonly List<Emblem> All = new()
    {
        // --- currently unlockable ---
        new Emblem(FirstLowball, McColorCodes.GOLD + "⚖",
            "Lowballer", "Created your first lowball offer.", AchievementCategory),
        new Emblem(BazaarFlipProfit, McColorCodes.GREEN + "⇗",
            "Bazaar Baron", "Closed your first profitable bazaar flip.", AchievementCategory),
        new Emblem(BazaarFlipLoss, McColorCodes.RED + "⇘",
            "Battle Scar", "Closed a bazaar flip at a loss - it happens to the best of us.", AchievementCategory),
        // --- suggested extras ---
        new Emblem(Whale, McColorCodes.AQUA + "❖",
            "Whale", "Land a single bazaar flip worth 100M+ coins of profit.", AchievementCategory),
        // --- role based ---
        new Emblem(Moderator, ModeratorSymbol,
            "Moderator", "Available to verified Coflnet moderators.", ModeratorCategory),
        // --- accumulated Premium time (Premium+ time counts towards these too) ---
        new Emblem(PremiumSixMonths, McColorCodes.GREEN + "Ⓟ",
            "6-Month Premium", "Accumulated at least 6 months of Premium or Premium+ time.", PremiumCategory),
        new Emblem(PremiumOneYear, McColorCodes.DARK_GREEN + "Ⓟ",
            "1-Year Premium", "Accumulated at least 1 year of Premium or Premium+ time.", PremiumCategory),
        new Emblem(PremiumTwoYears, McColorCodes.AQUA + "Ⓟ",
            "2-Year Premium", "Accumulated at least 2 years of Premium or Premium+ time.", PremiumCategory),
        new Emblem(PremiumThreeYears, McColorCodes.GOLD + "Ⓟ",
            "3-Year Premium", "Accumulated at least 3 years of Premium or Premium+ time.", PremiumCategory),
        // --- accumulated Premium+ time ---
        new Emblem(PremiumPlusSixMonths, McColorCodes.LIGHT_PURPLE + "✚",
            "6-Month Premium+", "Accumulated at least 6 months of Premium+ time.", PremiumPlusCategory),
        new Emblem(PremiumPlusOneYear, McColorCodes.DARK_PURPLE + "✚",
            "1-Year Premium+", "Accumulated at least 1 year of Premium+ time.", PremiumPlusCategory),
        new Emblem(PremiumPlusTwoYears, McColorCodes.BLUE + "✚",
            "2-Year Premium+", "Accumulated at least 2 years of Premium+ time.", PremiumPlusCategory),
        new Emblem(PremiumPlusThreeYears, McColorCodes.GOLD + "✚",
            "3-Year Premium+", "Accumulated at least 3 years of Premium+ time.", PremiumPlusCategory),
        // --- Pre API purchases ---
        new Emblem(PreApiOnePurchase, McColorCodes.GREEN + "♛",
            "Pre API x1", "Purchased Pre API access at least once.", PreApiCategory),
        new Emblem(PreApiFivePurchases, McColorCodes.AQUA + "♛",
            "Pre API x5", "Purchased Pre API access at least 5 times.", PreApiCategory),
        new Emblem(PreApiTenPurchases, McColorCodes.LIGHT_PURPLE + "♛",
            "Pre API x10", "Purchased Pre API access at least 10 times.", PreApiCategory),
        new Emblem(PreApiTwentyPurchases, McColorCodes.GOLD + "♛",
            "Pre API x20", "Purchased Pre API access at least 20 times.", PreApiCategory),
        // --- account age (granted automatically once the account is old enough) ---
        new Emblem(OneYearVeteran, McColorCodes.GREEN + "✦",
            "One Year Veteran", "Your Coflnet account is at least 1 year old. Thanks for flipping with us!", AccountAgeCategory),
        new Emblem(ThreeYearVeteran, McColorCodes.AQUA + "✶",
            "Three Year Veteran", "Your Coflnet account is at least 3 years old.", AccountAgeCategory),
        new Emblem(FiveYearVeteran, McColorCodes.GOLD + "❂",
            "Five Year Veteran", "Your Coflnet account is at least 5 years old.", AccountAgeCategory),
        // --- mysterious (no reveal of the unlock condition, not auto granted yet) ---
        new Emblem(NightOwl, McColorCodes.DARK_PURPLE + "☾",
            "Night Owl", "A mystery waiting in the small hours.", MysteryCategory, mysterious: true),
        new Emblem(DiamondHands, McColorCodes.BLUE + "♦",
            "Diamond Hands", "A mystery for those who never let go.", MysteryCategory, mysterious: true),
    };

    public static Emblem GetById(string id)
    {
        return All.FirstOrDefault(e => e.Id == id);
    }
}
