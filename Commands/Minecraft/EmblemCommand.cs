using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Show and equip emblems you unlocked",
        "Emblems are little badges you earn by playing.",
        "The one you equip shows in front of your chat messages.",
        "Usage: /cofl emblem to list, /cofl emblem set <id> to equip")]
    public class EmblemCommand : McCommand
    {
        public override bool IsPublic => true;

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = (string.IsNullOrWhiteSpace(arguments) ? string.Empty : Convert<string>(arguments) ?? string.Empty).Trim();
            var service = socket.GetService<EmblemService>();

            if (args == "clear")
            {
                socket.AccountInfo.Emblem = null;
                await socket.sessionLifesycle.AccountInfo.Update();
                socket.Dialog(db => db.MsgLine($"{McColorCodes.GRAY}Cleared your emblem. Nothing is shown in front of your chat messages anymore."));
                return;
            }
            if (args.StartsWith("set "))
            {
                var id = args.Substring("set ".Length).Trim();
                var emblem = Emblems.GetById(id);
                if (emblem == null)
                {
                    socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Unknown emblem `{id}`.")
                        .CoflCommand<EmblemCommand>($"{McColorCodes.GRAY}[See your emblems]", "", "Open the emblem menu"));
                    return;
                }
                var unlocked = await service.GetUnlockedForSocket(socket, forceRefresh: true);
                if (!unlocked.Contains(id))
                {
                    socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}You haven't unlocked {emblem.Name} yet."));
                    return;
                }
                socket.AccountInfo.Emblem = emblem.Symbol;
                await socket.sessionLifesycle.AccountInfo.Update();
                socket.Dialog(db => db.MsgLine($"{McColorCodes.GREEN}Equipped {emblem.Symbol} {McColorCodes.GREEN}{emblem.Name}{McColorCodes.GRAY}. It now shows in front of your chat messages."));
                return;
            }

            var unlockedSet = await service.GetUnlockedForSocket(socket);
            var equipped = socket.AccountInfo?.Emblem;
            var unlockedCount = Emblems.All.Count(e => unlockedSet.Contains(e.Id));
            socket.Dialog(db => db
                .MsgLine($"{McColorCodes.GOLD}{McColorCodes.BOLD}Emblems {McColorCodes.RESET}{McColorCodes.GRAY}({unlockedCount}/{Emblems.All.Count} unlocked)")
                .MsgLine($"{McColorCodes.GRAY}The emblem you equip is shown in front of your chat messages.")
                .ForEach(Emblems.All, (d, emblem) =>
                {
                    if (unlockedSet.Contains(emblem.Id))
                    {
                        d.Msg($"{emblem.Symbol} {McColorCodes.GREEN}{emblem.Name} {McColorCodes.GRAY}- {emblem.Description} ", null, emblem.Description);
                        if (equipped == emblem.Symbol)
                            d.MsgLine($"{McColorCodes.YELLOW}[equipped]");
                        else
                            d.CoflCommand<EmblemCommand>($"{McColorCodes.AQUA}[equip]", $"set {emblem.Id}", $"Show {emblem.Name} in front of your chat messages").LineBreak();
                    }
                    else if (emblem.Mysterious)
                    {
                        d.MsgLine($"{McColorCodes.DARK_GRAY}{emblem.Symbol} ??? {McColorCodes.GRAY}- {McColorCodes.DARK_GRAY}This emblem is a mystery. Keep playing to discover it.", null, "Mysterious emblem - the unlock condition is a surprise");
                    }
                    else
                    {
                        d.MsgLine($"{McColorCodes.DARK_GRAY}{emblem.Symbol} {emblem.Name} {McColorCodes.GRAY}- {McColorCodes.DARK_GRAY}{emblem.Description} {McColorCodes.RED}[locked]", null, emblem.Description);
                    }
                })
                .If(() => !string.IsNullOrEmpty(equipped), d =>
                    d.CoflCommand<EmblemCommand>($"{McColorCodes.GRAY}[Clear equipped emblem]", "clear", "Stop showing an emblem in front of your chat messages")));
        }
    }
}
