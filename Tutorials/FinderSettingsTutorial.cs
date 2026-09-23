using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class FinderSettingsTutorial : MacroBotTutorial
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        if(socket.Settings.WhiteList.Count > 0)
            return; // seems like the user is already familiar with the settings
        builder.MsgLine($"To configure which flips you get please set your finders with {McColorCodes.AQUA}/cofl set finders SNIPER,SNIPER_MEDIAN,BAZAAR,AI")
        .MsgLine($"Remove any that you don't want, here is what kind of flips they represent:")
            .MsgLine($"{McColorCodes.YELLOW}SNIPER: {McColorCodes.GRAY}shows lowest bin based items", null, $"That means they are undervalued compared to the current market but that market may be overvalued")
            .MsgLine($"{McColorCodes.YELLOW}SNIPER_MEDIAN: {McColorCodes.GRAY}shows median based items", null, $"Items are compared to their normal price but it may take a few hours to return to that price")
            .MsgLine($"{McColorCodes.YELLOW}BAZAAR: {McColorCodes.GRAY}shows bazaar flips based on whats currrently insta sold/bought most")
            .MsgLine($"{McColorCodes.YELLOW}AI{McColorCodes.GRAY} special finder for complex items may occasionally be wrong but can find good flips that other finders miss");
    }
}