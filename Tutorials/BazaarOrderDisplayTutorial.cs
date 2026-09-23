using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class BazaarOrderDisplayTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine("§6Your Bazaar orders now appear in info display 2. SkyBazaar updates their fill amounts automatically.",
                "/managebazaarorders", "View all current orders")
            .MsgLine("§7Use /cofl displays to move or resize it. Open chat or your inventory to hover or click its lines.",
                "/cofl displays", "Edit display layout")
            .MsgLine("§c[Disable Bazaar order display]", BazaarOrderDisplay.DisableCommand,
                "Saved preference. Re-enable with /cofl set modhideBazaarOrderDisplay false");
    }
}
