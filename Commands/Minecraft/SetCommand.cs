using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using System.Linq;
using static Coflnet.Sky.Commands.Shared.SettingsUpdater;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Api.Models.Mod;
using System.Collections.Generic;
using static Coflnet.Sky.Core.LowPricedAuction;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Sets a setting",
    "Usage: /cl set <setting> <value>",
    "Suggests corrections in case you have a typo",
    "Use only /cl set to get a list of all settings",
    "The default view allows you to change settings",
    "by clicking on the options next to them")]
public class SetCommand : McCommand
{
    public override bool IsPublic => true;
    internal static readonly SettingsUpdater updater = new SettingsUpdater();
    public static IEnumerable<string> Options => updater.Options();
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
            if (updater.IsLoreKey(name))
            {
                await UpdateLore(socket, name, newValue, page);
                return;
            }
            if (IsSignedOutPrivacyKey(socket, name))
            {
                SendPrivacyNeedsAccount(socket);
                return;
            }
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
            socket.Settings.Changer = socket.SessionInfo.ConnectionId;
            socket.Settings.LastChanged = name;
            await socket.sessionLifesycle.SaveFlipSettings();
            var doc = updater.GetDocFor(name);
            if (doc.RealName == nameof(ModSettings.HideBazaarOrderDisplay))
                Coflnet.Sky.ModCommands.Services.BazaarOrderDisplay.Send(socket);
            socket.SendMessage(new ChatPart($"{COFLNET}Set {McColorCodes.AQUA}{doc.RealName}{DEFAULT_COLOR} to {McColorCodes.WHITE}{finalValue}", null, doc.Info));
            if (page > 0)
                await PrintSettingsPage(socket, page);
            if (arguments.Contains("modtimer", StringComparison.CurrentCultureIgnoreCase))
                socket.sessionLifesycle.StartTimer(12);
            if (arguments.StartsWith("privacydisable", StringComparison.CurrentCultureIgnoreCase))
            {
                ProducePrivacyChangeForState(socket);
            }

            socket.GetService<FlipperService>().UpdateFilterSumaries();
        }
        catch (NoLoginException e)
        {
            // expected for sockets that have not logged in, tell the user how to fix it without logging an error
            socket.SendMessage(new ChatPart(COFLNET + McColorCodes.RED + e.Message));
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

    private static bool IsSignedOutPrivacyKey(MinecraftSocket socket, string name)
    {
        return socket.sessionLifesycle?.IsSignedOut == true && updater.IsPrivacyKey(name);
    }

    private void SendPrivacyNeedsAccount(MinecraftSocket socket)
    {
        socket.SendMessage(new ChatPart($"{COFLNET}{McColorCodes.RED}Privacy settings are bound to an account. "
            + $"Log in with {McColorCodes.AQUA}/cofl login{McColorCodes.RED} to change them", "/cofl login", "Click to log in"));
    }

    /// <summary>
    /// Lore settings go through the session so they share its lock: saved to the account when signed in, otherwise kept for the mod instance
    /// </summary>
    private async Task UpdateLore(MinecraftSocket socket, string name, string newValue, int page)
    {
        var lifecycle = socket.sessionLifesycle ?? throw new NoLoginException();
        var (saved, value) = await lifecycle.UpdateLoreSetting(name, newValue);
        if (!saved)
            return;
        if (!lifecycle.IsSignedOut)
            LoreCommand.ForgetCachedSettings(socket.UserId);
        var doc = updater.GetDocFor(name);
        socket.SendMessage(new ChatPart($"{COFLNET}Set {McColorCodes.AQUA}{doc.RealName}{DEFAULT_COLOR} to {McColorCodes.WHITE}{value}", null, doc.Info));
        if (page > 0)
            await PrintSettingsPage(socket, page);
    }

    private static void ProducePrivacyChangeForState(MinecraftSocket socket)
    {
        socket.GetService<IStateUpdateService>().Produce(socket.SessionInfo?.McName, new UpdateMessage()
        {
            Kind = UpdateMessage.UpdateKind.Setting,
            Settings = new()
            {
                DisableTradeTracking = socket.sessionLifesycle.PrivacySettings.Value.DisableTradeStoring,
                DisableKuudraTracking = socket.sessionLifesycle.PrivacySettings.Value.DisableKuudraTracking,
                DisableBazaarTracking = socket.sessionLifesycle.PrivacySettings.Value.DisableBazaarTracking
            },
            UserId = socket.UserId
        });
    }

    private static async Task<int> PrintSettingsPage(MinecraftSocket socket, int page)
    {
        var pageSize = 10;
        Console.WriteLine("page" + page);
        Func<string, string> formatSh = v => McColorCodes.GRAY + " (" + McColorCodes.GREEN + v + McColorCodes.GRAY + ")";
        var pageEntries = updater.ModOptions.Where(o => !o.Value.Hide).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var instanceDescription = await LoadInstanceDescription(socket, pageEntries.Any(o => updater.IsLoreKey(o.Key)));
        var options = await Task.WhenAll(pageEntries.Select(async o =>
        {
            var current = await updater.GetCurrentValue(socket, o.Key, instanceDescription);
            return (o, current);
        }));

        socket.Dialog(db => db.MsgLine($"Available settings are (page {page}):",
            "/cofl set " + (page + 1),
            $"These are available settings, the format is:\n{McColorCodes.AQUA}key{formatSh("shortVersion")}{McColorCodes.GRAY} Description\n"
            + "click to get next page")
            .ForEach(options, (db, setting) =>
            {
                var metaData = setting.o.Value;
                var shortHandAddition = !string.IsNullOrEmpty(metaData.ShortHand) ? formatSh(metaData.ShortHand) : "";
                var likelyTargetValue = metaData.Type == "Boolean" ? !(bool)setting.current : setting.current;
                db.CoflCommand<SetCommand>($"{McColorCodes.AQUA}{setting.o.Key}{shortHandAddition}: {McColorCodes.GREEN}{GetCurrent(setting)}",
                    $"{page} {setting.o.Key} {likelyTargetValue}",
                    $"Click to set {McColorCodes.AQUA}{setting.o.Key}{McColorCodes.GRAY} to {McColorCodes.GREEN}{likelyTargetValue}");
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
                    db.CoflCommand<SetCommand>($"{McColorCodes.YELLOW}{McColorCodes.ITALIC}Toggle",
                    $"{page} {setting.o.Key} {!current}",
                    $"Click to set {McColorCodes.AQUA}{setting.o.Key}{McColorCodes.GRAY} to {McColorCodes.GREEN}{!current}");
                }
                if (setting.o.Key == "finders")
                {
                    PrintFinderOption(page, db, setting, metaData, FinderType.SNIPER, " LBin");
                    PrintFinderOption(page, db, setting, metaData, FinderType.FLIPPER_AND_SNIPERS, " Median ");
                    PrintFinderOption(page, db, setting, metaData, FinderType.FLIPPER_AND_SNIPERS | FinderType.STONKS, $"{McColorCodes.RED}Risky");
                }

                db.MsgLine($" {McColorCodes.GRAY}{metaData.Info}");
            })
        );
        return page;

        static void PrintFinderOption(int page, DialogBuilder db, (KeyValuePair<string, SettingDoc> o, object current) setting, SettingDoc metaData, FinderType lbin, string label)
        {
            db.CoflCommand<SetCommand>($"{McColorCodes.YELLOW}{McColorCodes.ITALIC}{label}",
            $"{page} {setting.o.Key} {lbin}",
            $"Click to set {McColorCodes.AQUA}{setting.o.Key}{McColorCodes.GRAY} to {McColorCodes.GREEN}{lbin}");
        }
    }

    private static async Task<DescriptionSetting> LoadInstanceDescription(MinecraftSocket socket, bool pageHasLoreKeys)
    {
        if (!pageHasLoreKeys || !socket.sessionLifesycle.IsSignedOut || !socket.sessionLifesycle.SavesSettingsForInstance)
            return null;
        return await socket.GetService<IInstanceSettingsService>().GetDescription(socket.SessionInfo.SessionId);
    }

    private static object GetCurrent((KeyValuePair<string, SettingDoc> o, object current) setting)
    {
        if (setting.current is HashSet<string> hashSet)
            return string.Join(", ", hashSet);
        return setting.current;
    }

    private static void PrintChangeCommand(MinecraftSocket socket, int page, DialogBuilder db, (KeyValuePair<string, SettingDoc> o, object current) setting, SettingDoc metaData, long current, int changeAmount)
    {
        db.CoflCommand<SetCommand>($"{McColorCodes.RED}-{socket.formatProvider.FormatPrice(changeAmount)}",
            $"{page} {setting.o.Key} {current - changeAmount}",
            $"Click to set {McColorCodes.AQUA}{setting.o.Key}{McColorCodes.GRAY} to {McColorCodes.GREEN}{socket.formatProvider.FormatPrice(current - changeAmount)}");
        db.CoflCommand<SetCommand>($"{McColorCodes.YELLOW}+{socket.formatProvider.FormatPrice(changeAmount)}",
            $"{page} {setting.o.Key} {current + changeAmount}",
            $"Click to set {McColorCodes.AQUA}{setting.o.Key}{McColorCodes.GRAY} to {McColorCodes.GREEN}{socket.formatProvider.FormatPrice(current + changeAmount)}");
    }
}