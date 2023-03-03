using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class LoreCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var service = socket.GetService<SettingsService>();
            var settings = await service.GetCurrentValue<DescriptionSetting>(socket.UserId, "description", () =>
            {
                return DescriptionSetting.Default;
            });
            var args = Convert<string>(arguments).Split(' ');

            if (args.Length < 2)
            {
                SendCurrentState(socket, settings);
                return;
            }

            var line = int.Parse(args[1]);
            if (!Enum.TryParse<DescriptionField>(args[2], true, out DescriptionField field))
            {
                socket.SendMessage(McColorCodes.RED + "Usage: " + McColorCodes.GRAY + "lore <line> <FieldType>");
                return;
            }
            Console.WriteLine("applying " + arguments);
            switch (args[0])
            {
                case "add":
                    while (line >= settings.Fields.Count)
                        settings.Fields.Add(new());
                    settings.Fields[line].Add(field);
                    break;
                case "rm":
                    if(line >= settings.Fields.Count){
                        socket.SendMessage(McColorCodes.RED + "Not possible to remove field from line " + line + " as it doesn't exist");
                        return;
                    }
                    settings.Fields[line].Remove(field);
                    if (settings.Fields[line].Count == 0)
                        settings.Fields.RemoveAt(line);
                    break;
                case "up":
                    if (line == 0)
                        break;
                    settings.Fields[line].Remove(field);
                    settings.Fields[line - 1].Add(field);
                    // remove empty lines
                    if (settings.Fields[line].Count == 0)
                        settings.Fields.RemoveAt(line);
                    break;
                case "down":
                    if (line == settings.Fields.Count - 1)
                        settings.Fields.Add(new());
                    settings.Fields[line].Remove(field);
                    settings.Fields[line + 1].Add(field);
                    // remove empty lines
                    if (settings.Fields[line].Count == 0)
                        settings.Fields.RemoveAt(line);
                    break;
                case "left":
                    settings.Fields[line].Remove(field);
                    settings.Fields[line].Insert(0, field);
                    break;
            }
            SendCurrentState(socket, settings);
            await service.UpdateSetting(socket.UserId, "description", settings);
        }

        private static void SendCurrentState(MinecraftSocket socket, DescriptionSetting settings)
        {
            var lineNum = 0;
            var colorIndex = 0;
            var optionsToAdd = Enum.GetValues<DescriptionField>().Where(e=>(int)e < 9000).GroupBy(e => (int)e).Select(g=>g.First()).ToList();
            var d = DialogBuilder.New.Break.ForEach(settings.Fields, (d, line) =>
            {
                var elementInLine = 0;
                d.ForEach(line, (d, f) =>
                {
                    d.Msg($" {McColorCodes.GRAY}[{McColorCodes.AQUA}{f.ToString()}{McColorCodes.GRAY}]")
                    .CoflCommand<LoreCommand>(McColorCodes.RED + "rm", $"rm {lineNum} {f}", $"Remove {f} from line {lineNum + 1}");

                    if (lineNum > 0)
                        d.CoflCommand<LoreCommand>(McColorCodes.GREEN + "⬆ ", $"up {lineNum} {f}", $"Move {f} up to line {lineNum}");
                    if(lineNum < settings.Fields.Count)
                        d.CoflCommand<LoreCommand>(McColorCodes.GREEN + "⬇", $"down {lineNum} {f}", $"Move {f} down to line {lineNum + 2}");
                    if (elementInLine > 0)
                        d.CoflCommand<LoreCommand>(McColorCodes.YELLOW + "<-", $"left {lineNum} {f}", $"Move {f} to the left");
                    elementInLine++;
                }).LineBreak();
                lineNum++;
            }).Break.MsgLine("Add one of the following stats").ForEach(optionsToAdd, (d, f) =>
            {
                var color = (colorIndex++ % 3) switch
                {
                    0 => McColorCodes.LIGHT_PURPLE,
                    1 => McColorCodes.AQUA,
                    _ => McColorCodes.YELLOW
                };
                d.CoflCommand<LoreCommand>(color + f.ToString(), $"add {lineNum} {f}", $"Add {f} to the next line").Msg(" ");
            });
            socket.SendMessage(d.Build());
        }
    }
}