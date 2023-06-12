using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Linq;
using static Coflnet.Sky.Commands.Shared.SettingsUpdater;
using Coflnet.Sky.ModCommands.Dialogs;
using System.Collections.Generic;
using static Coflnet.Sky.Core.LowPricedAuction;

namespace Coflnet.Sky.Commands.MC
{
    public class SetCommand : McCommand
    {
        public override bool IsPublic => true;
        private static SettingsUpdater updater = new SettingsUpdater();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            try
            {
                if (arguments.Length > 300)
                    throw new CoflnetException("to_long", "the settings value is too long");
                arguments = JsonConvert.DeserializeObject<string>(arguments).Replace('$', '§');
                Console.WriteLine(arguments);
                if (int.TryParse(arguments.Split(' ')[0], out var page) || arguments.Length == 0)
                {
                    if (page < 1)
                        page = 1;
                    if (arguments.Length < 5)
                    {
                        await PrintSettingsPage(socket, page);
                        return;
                    }
                    arguments = arguments.Substring(arguments.IndexOf(' ') + 1);
                }
                var name = arguments.Split(' ')[0];
                var newValue = arguments.Substring(name.Length).Trim();
                object finalValue;
                try
                {
                    finalValue = await updater.Update(socket, name, newValue);
                }
                catch (UnknownSettingException e)
                {
                    var altExecution = arguments.Replace(e.Passed, e.Closest);
                    socket.Dialog(db => db.CoflCommand<SetCommand>(
                        $"the setting {McColorCodes.DARK_RED}{e.Passed}{DEFAULT_COLOR} doesn't exist, most similar is {McColorCodes.AQUA}{e.Closest}{DEFAULT_COLOR}",
                        altExecution, $"Click to rerun {McColorCodes.AQUA}/cofl set {altExecution}"));
                    return;
                }
                if (string.IsNullOrEmpty(socket.sessionLifesycle.UserId))
                    socket.SendMessage(new ChatPart($"{COFLNET}You are not logged in, setting will reset when you stop the connection"));
                else
                {
                    var service = DiHandler.ServiceProvider.GetRequiredService<SettingsService>();
                    socket.Settings.Changer = socket.SessionInfo.ConnectionId;
                    await service.UpdateSetting(socket.sessionLifesycle.UserId, "flipSettings", socket.Settings);
                }
                var doc = updater.GetDocFor(name);
                socket.SendMessage(new ChatPart($"{COFLNET}Set {McColorCodes.AQUA}{doc.RealName}{DEFAULT_COLOR} to {McColorCodes.WHITE}{finalValue}", null, doc.Info));
                if (page > 0)
                    await PrintSettingsPage(socket, page);
            }
            catch (CoflnetException e)
            {
                socket.SendMessage(new ChatPart(COFLNET + McColorCodes.RED + e.Message));
                dev.Logger.Instance.Error(e, "set setting");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "set setting");
                socket.SendMessage(new ChatPart(COFLNET + "an error occured while executing that"));

            }
        }

        private static async Task<int> PrintSettingsPage(MinecraftSocket socket, int page)
        {
            var pageSize = 10;
            Console.WriteLine("page" + page);
            Func<string, string> formatSh = v => MC.McColorCodes.GRAY + " (" + MC.McColorCodes.GREEN + v + MC.McColorCodes.GRAY + ")";
            var options = await Task.WhenAll(updater.ModOptions.Where(o => !o.Value.Hide).Select(async o =>
            {
                var shortHandAddition = !string.IsNullOrEmpty(o.Value.ShortHand) ? formatSh(o.Value.ShortHand) : "";
                var current = await updater.GetCurrentValue(socket, o.Key);
                return (o, current);//$"{MC.McColorCodes.AQUA}{o.Key}{shortHandAddition}: {McColorCodes.GREEN}{current} {MC.McColorCodes.GRAY}{o.Value.Info}";
            }).Skip((page - 1) * pageSize).Take(pageSize));

            socket.Dialog(db => db.MsgLine($"Available settings are (page {page}):",
                "/cofl set " + (page + 1),
                $"These are available settings, the format is:\n{McColorCodes.AQUA}key{formatSh("shortVersion")}{McColorCodes.GRAY} Description\n"
                + "click to get next page")
                .ForEach(options, (db, setting) =>
                {
                    var metaData = setting.o.Value;
                    var shortHandAddition = !string.IsNullOrEmpty(metaData.ShortHand) ? formatSh(metaData.ShortHand) : "";
                    var likelyTargetValue = metaData.Type == "Boolean" ? !(bool)setting.current : setting.current;
                    db.CoflCommand<SetCommand>($"{MC.McColorCodes.AQUA}{setting.o.Key}{shortHandAddition}: {McColorCodes.GREEN}{setting.current}",
                        $"{page} {setting.o.Key} {likelyTargetValue}",
                        $"Click to set {MC.McColorCodes.AQUA}{setting.o.Key}{MC.McColorCodes.GRAY} to {MC.McColorCodes.GREEN}{likelyTargetValue}");
                    if (metaData.Type == "Int64")
                    {
                        var current = (long)setting.current;
                        var changeAmount = 100000;
                        // change by 100k
                        PrintChangeCommand(socket, page, db, setting, metaData, current, changeAmount);
                    }
                    if (metaData.Type == "Int32")
                    {
                        PrintChangeCommand(socket, page, db, setting, metaData, (int)setting.current, 1);
                    }
                    if (metaData.Type == "Single")
                    {
                        PrintChangeCommand(socket, page, db, setting, metaData, (int)(float)setting.current, 1);
                    }
                    if (metaData.Type == "Double")
                    {
                        PrintChangeCommand(socket, page, db, setting, metaData, (int)(double)setting.current, 1);
                    }
                    if (metaData.Type == "Boolean-")
                    {
                        var current = (bool)setting.current;
                        db.CoflCommand<SetCommand>($"{MC.McColorCodes.YELLOW}{McColorCodes.ITALIC}Toggle",
                        $"{page} {setting.o.Key} {!current}",
                        $"Click to set {MC.McColorCodes.AQUA}{setting.o.Key}{MC.McColorCodes.GRAY} to {MC.McColorCodes.GREEN}{!current}");
                    }
                    if (setting.o.Key == "finders")
                    {
                        PrintFinderOption(page, db, setting, metaData, FinderType.SNIPER, " LBin");
                        PrintFinderOption(page, db, setting, metaData, FinderType.FLIPPER_AND_SNIPERS, " Median ");
                        PrintFinderOption(page, db, setting, metaData, FinderType.FLIPPER_AND_SNIPERS | FinderType.STONKS, $"{McColorCodes.RED}Risky");
                    }

                    db.MsgLine($" {MC.McColorCodes.GRAY}{metaData.Info}");
                })
            );
            return page;

            static void PrintFinderOption(int page, DialogBuilder db, (KeyValuePair<string, SettingDoc> o, object current) setting, SettingDoc metaData, FinderType lbin, string label)
            {
                db.CoflCommand<SetCommand>($"{MC.McColorCodes.YELLOW}{McColorCodes.ITALIC}{label}",
                $"{page} {setting.o.Key} {lbin}",
                $"Click to set {MC.McColorCodes.AQUA}{setting.o.Key}{MC.McColorCodes.GRAY} to {MC.McColorCodes.GREEN}{lbin}");
            }
        }

        private static void PrintChangeCommand(MinecraftSocket socket, int page, DialogBuilder db, (KeyValuePair<string, SettingDoc> o, object current) setting, SettingDoc metaData, long current, int changeAmount)
        {
            db.CoflCommand<SetCommand>($"{McColorCodes.RED}-{socket.formatProvider.FormatPrice(changeAmount)}",
                $"{page} {setting.o.Key} {current - changeAmount}",
                $"Click to set {MC.McColorCodes.AQUA}{setting.o.Key}{MC.McColorCodes.GRAY} to {MC.McColorCodes.GREEN}{socket.formatProvider.FormatPrice(current - changeAmount)}");
            db.CoflCommand<SetCommand>($"{McColorCodes.YELLOW}+{socket.formatProvider.FormatPrice(changeAmount)}",
                $"{page} {setting.o.Key} {current + changeAmount}",
                $"Click to set {MC.McColorCodes.AQUA}{setting.o.Key}{MC.McColorCodes.GRAY} to {MC.McColorCodes.GREEN}{socket.formatProvider.FormatPrice(current + changeAmount)}");
        }
    }
}