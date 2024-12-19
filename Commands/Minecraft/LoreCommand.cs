using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Change whats appended to item lore",
        "Displays a chat menu to modify whats put in what line",
        "Some options may take longer to load than others")]
    public class LoreCommand : McCommand
    {
        public override bool IsPublic => true;
        private ConcurrentDictionary<string, (DescriptionSetting, DateTime)> descriptionCache = new();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var service = socket.GetService<SettingsService>();
            if (!descriptionCache.TryGetValue(socket.UserId, out var cache) || cache.Item2.AddMinutes(1) < DateTime.UtcNow)
            {
                cache.Item1 = await service.GetCurrentValue<DescriptionSetting>(socket.UserId, "description", () =>
                {
                    return DescriptionSetting.Default;
                });
                cache.Item2 = DateTime.UtcNow;
                descriptionCache[socket.UserId] = cache;
            }
            var settings = cache.Item1;
            if (arguments.StartsWith("\"{"))
            {
                // assume this is a full json settings object
                settings = Convert<DescriptionSetting>(arguments);
                await service.UpdateSetting(socket.UserId, "description", settings);
                SendCurrentState(socket, settings);
                socket.Dialog(db => db.MsgLine("Imported settings (check above)"));
                return;
            }
            var args = Convert<string>(arguments).Split(' ');

            if (args.Length == 1)
            {
                if (args[0] == "enable" || args[0] == "true")
                {
                    settings.Disabled = false;
                    await service.UpdateSetting(socket.UserId, "description", settings);
                    socket.SendMessage("Enabled data in lore");
                    return;
                }
                if (args[0] == "disable" || args[0] == "false")
                {
                    settings.Disabled = true;
                    await service.UpdateSetting(socket.UserId, "description", settings);
                    socket.SendMessage("Disabled data in lore");
                    return;
                }
            }
            var privacySettings = socket.sessionLifesycle.PrivacySettings;
            if (!privacySettings.Value.ExtendDescriptions)
            {
                socket.Dialog(db => db.MsgLine("You have disabled the display of additional information on items")
                    .CoflCommand<SetCommand>("[Click here to enable]", "privacyextendDescriptions true", "Enable the display of additional information on items"));
                return;
            }

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
                    if (line >= settings.Fields.Count)
                    {
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
            await service.UpdateSetting(socket.UserId, "description", settings);
            SendCurrentState(socket, settings);
        }

        private static void SendCurrentState(MinecraftSocket socket, DescriptionSetting settings)
        {
            var lineNum = 0;
            var colorIndex = 0;
            var optionsToAdd = Enum.GetValues<DescriptionField>().Where(e => (int)e < 9000).GroupBy(e => (int)e).Select(g => g.First()).ToList();
            var Lines = new List<DescriptionField>[] {
                [DescriptionField.LBIN, DescriptionField.MEDIAN, DescriptionField.CRAFT_COST, DescriptionField.BazaarBuy, DescriptionField.BazaarSell],
                [DescriptionField.LBIN_KEY,DescriptionField.MEDIAN_KEY,DescriptionField.ITEM_KEY,DescriptionField.FullCraftCost],
                [DescriptionField.EnchantCost, DescriptionField.GemValue, DescriptionField.ModifierCost, DescriptionField.FinderEstimates],
                [DescriptionField.VOLUME, DescriptionField.SpentOnAhFees, DescriptionField.KatUpgradeCost, DescriptionField.Volatility],
                [DescriptionField.TimeToSell, DescriptionField.InstaSellPrice, DescriptionField.PRICE_PAID, DescriptionField.LastSoldFor],
                [] };
            var existing = Lines.SelectMany(l => l).Append(DescriptionField.NONE).ToHashSet();
            Lines.Last().AddRange(optionsToAdd.Where(o => !existing.Contains(o)));
            var d = DialogBuilder.New.CoflCommand<SetCommand>($"Toggle ah filter highlighting", "loreHighlightFilterMatch", $"Toggle {(settings.HighlightFilterMatch ? "off" : "on")}")
            .Break.ForEach(settings.Fields, (d, line) =>
            {
                var elementInLine = 0;
                d.ForEach(line, (d, f) =>
                {
                    var explanation = GetDescriptionFromEnumValue(f);
                    d.Msg($" {McColorCodes.GRAY}[{McColorCodes.AQUA}{f}{McColorCodes.GRAY}]", null, explanation)
                    .CoflCommand<LoreCommand>(McColorCodes.RED + "rm", $"rm {lineNum} {f}", $"Remove {f} from line {lineNum + 1}");

                    if (lineNum > 0)
                        d.CoflCommand<LoreCommand>(McColorCodes.GREEN + "⬆ ", $"up {lineNum} {f}", $"Move {f} up to line {lineNum}");
                    if (lineNum < settings.Fields.Count)
                        d.CoflCommand<LoreCommand>(McColorCodes.GREEN + "⬇", $"down {lineNum} {f}", $"Move {f} down to line {lineNum + 2}");
                    if (elementInLine > 0)
                        d.CoflCommand<LoreCommand>(McColorCodes.YELLOW + "<-", $"left {lineNum} {f}", $"Move {f} to the left");
                    elementInLine++;
                }).LineBreak();
                lineNum++;
            }).Break.MsgLine("Add one of the following stats")
                .ForEach(Lines, (d, line) => d.ForEach(line, (d, f) =>
            {
                var color = (colorIndex++ % 3) switch
                {
                    0 => McColorCodes.LIGHT_PURPLE,
                    1 => McColorCodes.AQUA,
                    _ => McColorCodes.YELLOW
                };
                var explanation = GetDescriptionFromEnumValue(f);
                d.CoflCommand<LoreCommand>(color + f.ToString(), $"add {lineNum} {f}", $"Add {McColorCodes.AQUA}{f}{McColorCodes.RESET} to the next line\n" + explanation).Msg(" ");
            }).LineBreak());
            socket.SendMessage(d.Build());
        }

        public static string GetDescriptionFromEnumValue(Enum value)
        {
            FieldDescription attribute = value.GetType()
                .GetField(value.ToString())
                .GetCustomAttributes(typeof(FieldDescription), false)
                .SingleOrDefault() as FieldDescription;
            return attribute == null ? value.ToString() : string.Join("\n", attribute.Text);
        }
    }
}