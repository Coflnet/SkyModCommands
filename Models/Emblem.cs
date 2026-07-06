using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.ModCommands.Models;

/// <summary>
/// A displayable emblem (achievement badge). "Emblem" (the symbol/name shown in chat) is purely a mod
/// presentation concern; the underlying unlock state is an achievement tracked in SkyUserState.
/// </summary>
public class Emblem
{
    /// <summary>
    /// The achievement this emblem represents. Its value MUST match the name of a member of the
    /// authoritative <c>Achievement</c> enum in SkyUserState (generated into this project as
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
    /// When true and the emblem is still locked it is shown as "???" - the unlock condition is a surprise.
    /// </summary>
    public bool Mysterious { get; }

    public Emblem(string id, string symbol, string name, string description, bool mysterious = false)
    {
        Id = id;
        Symbol = symbol;
        Name = name;
        Description = description;
        Mysterious = mysterious;
    }
}

/// <summary>
/// The catalog of all emblems. The id constants mirror the authoritative <c>Achievement</c> enum names
/// from SkyUserState - keep them in sync (see <see cref="Emblem.Id"/>).
/// </summary>
public static class Emblems
{
    public const string FirstLowball = "FirstLowball";
    public const string BazaarFlipProfit = "BazaarFlipProfit";
    public const string BazaarFlipLoss = "BazaarFlipLoss";
    public const string Whale = "Whale";
    public const string NightOwl = "NightOwl";
    public const string DiamondHands = "DiamondHands";

    /// <summary>
    /// All emblems in display order. Colors are baked into the symbol so it keeps its color when
    /// prepended in front of the rank color in chat.
    /// </summary>
    public static readonly List<Emblem> All = new()
    {
        // --- currently unlockable ---
        new Emblem(FirstLowball, McColorCodes.GOLD + "⚖",
            "Lowballer", "Created your first lowball offer."),
        new Emblem(BazaarFlipProfit, McColorCodes.GREEN + "⇗",
            "Bazaar Baron", "Closed your first profitable bazaar flip."),
        new Emblem(BazaarFlipLoss, McColorCodes.RED + "⇘",
            "Battle Scar", "Closed a bazaar flip at a loss - it happens to the best of us."),
        // --- suggested extras ---
        new Emblem(Whale, McColorCodes.AQUA + "❖",
            "Whale", "Land a single bazaar flip worth 100M+ coins of profit."),
        // --- mysterious (no reveal of the unlock condition, not auto granted yet) ---
        new Emblem(NightOwl, McColorCodes.DARK_PURPLE + "☾",
            "Night Owl", "A mystery waiting in the small hours.", mysterious: true),
        new Emblem(DiamondHands, McColorCodes.BLUE + "♦",
            "Diamond Hands", "A mystery for those who never let go.", mysterious: true),
    };

    public static Emblem GetById(string id)
    {
        return All.FirstOrDefault(e => e.Id == id);
    }
}
