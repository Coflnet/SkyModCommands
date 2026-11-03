using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Change whats appended to item lore",
        "Displays a chat menu to modify whats put in what line",
        "Some options may take longer to load than others")]
    public class LoreCommand : McCommand
    {
        public override bool IsPublic => true;
        private static ConcurrentDictionary<string, (DescriptionSetting, DateTime)> descriptionCache = new();
        /// <summary>
        /// Drops the cached settings of an user so changes made elsewhere are not overwritten
        /// </summary>
        internal static void ForgetCachedSettings(string userId) => descriptionCache.TryRemove(userId, out _);
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (socket.sessionLifesycle == null)
                throw new NoLoginException();
            var settings = await LoadSettings(socket, arguments.Trim('"') == "json");
            if (SettingsUpdater.CleanupJunkEntries(settings.DisableInfoIn))
                settings = await Save(socket, s => { SettingsUpdater.CleanupJunkEntries(s.DisableInfoIn); return s; }) ?? settings;
            if (arguments.StartsWith("\"{"))
            {
                // assume this is a full json settings object
                var imported = Convert<DescriptionSetting>(arguments);
                settings = await Save(socket, _ => imported);
                if (settings == null)
                    return;
                socket.Send(Response.Create("loreSettings", Newtonsoft.Json.JsonConvert.SerializeObject(settings)));
                SendCurrentState(socket, settings);
                socket.Dialog(db => db.MsgLine("Imported settings (check above)"));
                return;
            }
            if (arguments.Trim('"') == "json")
            {
                socket.Send(Response.Create("loreSettings", Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented)));
                return;
            }
            var args = Convert<string>(arguments).Split(' ');

            if (args.Length == 1)
            {
                if (args[0] == "enable" || args[0] == "true")
                {
                    if (await Save(socket, s => { s.Disabled = false; return s; }) != null)
                        socket.SendMessage("Enabled data in lore");
                    return;
                }
                if (args[0] == "disable" || args[0] == "false")
                {
                    if (await Save(socket, s => { s.Disabled = true; return s; }) != null)
                        socket.SendMessage("Disabled data in lore");
                    return;
                }
            }
            var privacySettings = socket.sessionLifesycle.PrivacySettings;
            if (privacySettings?.Value?.ExtendDescriptions == false)
            {
                socket.Dialog(db => db.MsgLine("You have disabled the display of additional information on items")
                    .CoflCommand<SetCommand>("[Click here to enable]", "privacyextendDescriptions true", "Enable the display of additional information on items"));
                return;
            }

            if (args.Length < 2)
            {
                socket.Send(Response.Create("getMods", 0));
                // Immediately send current state so the command is not slowed down by mod loading
                SendCurrentState(socket, settings);
                // Fire-and-forget: after a short delay, if Firmament is present send the warning as an addition
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    if (socket.SessionInfo.ModsFound != null && socket.SessionInfo.ModsFound.Any(m => m != null && m.ToLower().Contains("firmament")))
                    {
                        var d = DialogBuilder.New;
                        d.MsgLine($"{McColorCodes.RED}Warning: The Firmament setting `Show Lore Timers` interferes with auctions")
                            .Msg("[Click here for more info]", "https://discord.com/channels/267680588666896385/1448361704877719572/1448387977775681648", "get more info").LineBreak();
                        socket.SendMessage(d.Build());
                    }
                });
                return;
            }

            if (!int.TryParse(args[1], out var line) || line < 0)
                throw new CoflnetException("invalid_line", "Invalid arguments, try clicking on the action you want to perform after running `/cofl lore` instead of typing it yourself");
            if (!Enum.TryParse<DescriptionField>(args[2], true, out DescriptionField field))
            {
                socket.SendMessage(McColorCodes.RED + "Usage: " + McColorCodes.GRAY + "lore <line> <FieldType>");
                return;
            }
            Console.WriteLine("applying " + arguments);
            var updated = await Save(socket, s => ApplyLineEdit(socket, s, args[0], line, field) ? s : null);
            if (updated != null)
                SendCurrentState(socket, updated);
        }

        /// <summary>
        /// Loads the settings, signed-in users get a cached copy that is at most a minute old
        /// </summary>
        private static async Task<DescriptionSetting> LoadSettings(MinecraftSocket socket, bool forceFresh)
        {
            var lifecycle = socket.sessionLifesycle;
            if (lifecycle.IsSignedOut)
                return await lifecycle.GetDescriptionSetting();
            var userId = socket.UserId;
            if (!forceFresh && descriptionCache.TryGetValue(userId, out var cache) && cache.Item2.AddMinutes(1) >= DateTime.UtcNow)
                return cache.Item1;
            var settings = await lifecycle.GetDescriptionSetting();
            descriptionCache[userId] = (settings, DateTime.UtcNow);
            return settings;
        }

        /// <summary>
        /// Saves a change through the session, null when nothing was saved (opt-in missing or change rejected)
        /// </summary>
        private static async Task<DescriptionSetting> Save(MinecraftSocket socket, Func<DescriptionSetting, DescriptionSetting> change)
        {
            var saved = await socket.sessionLifesycle.UpdateDescriptionSetting(change);
            if (saved != null && !socket.sessionLifesycle.IsSignedOut)
                descriptionCache[socket.UserId] = (saved, DateTime.UtcNow);
            return saved;
        }

        /// <returns>false when the edit was rejected and the user has been told</returns>
        private static bool ApplyLineEdit(MinecraftSocket socket, DescriptionSetting settings, string action, int line, DescriptionField field)
        {
            switch (action)
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
                        return false;
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
            return true;
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