using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.ModCommands.Tutorials;

public class ChatRulesTutorial : TutorialBase
{
    public override void Trigger(DialogBuilder builder, IMinecraftSocket socket)
    {
        builder.MsgLine($"{McColorCodes.YELLOW}The chat rules are:")
            .MsgLine($" 1) {McColorCodes.AQUA}Be Nice", null, $"{McColorCodes.YELLOW}Just be nice to each other")
            .MsgLine($" 2) {McColorCodes.AQUA}Don't advertise something nobody asked for", null, $"{McColorCodes.YELLOW}This includes priming someone to ask something")
            .MsgLine($"Furthermore tfm rules apply (see hover text).", null, $"{McColorCodes.BOLD} Use English\nThe primary and only language of this server is English. Please keep all the conversations in English to allow us to moderate the chat easily.\n"
            + $"{McColorCodes.BOLD} Follow Discord's ToS\nMake sure to follow the Discord ToS and Community Guidelines as well as Hypixel Skyblock Rules at all times.\n"
            + $"{McColorCodes.BOLD} Be respectful\nRacism, Sexism, Homophobic, Transphobic, Antisemitism, Hate speech, slurs or any other toxic or discriminating behaviour will not be tolerated and will result in disciplinary action.\n"
            + $"{McColorCodes.BOLD} No IRL trading\nAll forms of IRL trading are bannable in this server (Including using this server's connections to DM people promoting IRL trading)\n"
            + $"{McColorCodes.BOLD} No exploits\nAny communication about exploits will not be tolerated. This includes but is not limited to: macroing, TFM server exploits, disadvantaging other users, etc.\n"
            + $"Any such talk may result in a 1+ day chat ban, or being blacklisted completely from the service.\n"
            + $"{McColorCodes.BOLD} Use common sense\nThis rule is pretty self explanatory. If you think your actions will receive negative reactions, refrain from doing them.");
    }
}
