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
                .MsgLine($"{McColorCodes.GRAY}Hover for details. Click an unlocked emblem to equip it.")
                .ForEach(Emblems.All.GroupBy(emblem => emblem.Category), (d, category) =>
                {
                    d.MsgLine($"{McColorCodes.GOLD}{McColorCodes.BOLD}{category.Key}{McColorCodes.RESET}");
                    d.ForEach(category, (row, emblem, index) =>
                    {
                        var hover = $"{McColorCodes.GOLD}{emblem.Name}\n{McColorCodes.GRAY}{emblem.Description}";
                        if (unlockedSet.Contains(emblem.Id))
                        {
                            if (equipped == emblem.Symbol)
                                row.Msg($"{McColorCodes.YELLOW}[{emblem.Symbol}{McColorCodes.YELLOW}] ", null, $"{hover}\n{McColorCodes.YELLOW}Equipped");
                            else
                                row.CoflCommand<EmblemCommand>($"{McColorCodes.GRAY}[{emblem.Symbol}{McColorCodes.GRAY}] ", $"set {emblem.Id}", $"{hover}\n{McColorCodes.AQUA}Click to equip");
                        }
                        else if (emblem.Mysterious)
                        {
                            row.Msg($"{McColorCodes.DARK_GRAY}[{emblem.Symbol}{McColorCodes.DARK_GRAY}] ", null,
                                $"{McColorCodes.DARK_GRAY}???\nThis emblem is a mystery. Keep playing to discover it.\n{McColorCodes.RED}Locked");
                        }
                        else
                        {
                            row.Msg($"{McColorCodes.DARK_GRAY}[{emblem.Symbol}{McColorCodes.DARK_GRAY}] ", null,
                                $"{hover}\n{McColorCodes.RED}Locked");
                        }

                        if ((index + 1) % 6 == 0)
                            row.LineBreak();
                    });
                    if (category.Count() % 6 != 0)
                        d.LineBreak();
                })
                .If(() => !string.IsNullOrEmpty(equipped), d =>
                    d.CoflCommand<EmblemCommand>($"{McColorCodes.GRAY}[Clear equipped emblem]", "clear", "Stop showing an emblem in front of your chat messages")));
        }
    }
}
