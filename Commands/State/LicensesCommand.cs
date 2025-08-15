using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Allows you to manage your account licenses",
    "Licenses allow you to open multiple connections at once",
    "If you have multiple accounts and want to configure",
    "which one takes the premium on your email first use",
    "/cofl license default <userName>")]
public class LicensesCommand : ListCommand<PublicLicenseWithName, List<PublicLicenseWithName>>
{
    protected override bool CanAddMultiple => false;
    protected override async Task DefaultAction(MinecraftSocket socket, string stringArgs)
    {
        var args = stringArgs.Split(' ');
        var command = args[0];
        if (command == "default")
        {
            if (args.Length == 1)
                throw new CoflnetException("no_username", "Please provide the username you want your account license to default to");
            var uuid = await socket.GetPlayerUuid(args[1]);
            var tierManager = socket.sessionLifesycle.TierManager;
            await tierManager.ChangeDefaultTo(uuid);
            socket.Dialog(db => db.MsgLine($"Changed default account you use your account premium on to {McColorCodes.AQUA}{args[1]}"));
            var newTier = await tierManager.GetCurrentTierWithExpire();
            socket.Dialog(db => db.MsgLine($"This connection is now {McColorCodes.AQUA}{newTier}"));
            return;
        }
        if (command == "refund")
        {
            var licenses = await socket.GetService<ITransactionApi>().TransactionUUserIdGetAsync(socket.UserId);
            var refund = licenses.FirstOrDefault(l => l.ProductId == "premium_plus-weeks" && l.TimeStamp > DateTime.UtcNow.AddDays(-10));
            if (refund == null)
            {
                socket.Dialog(db => db.MsgLine("You don't have a refundable license"));
                return;
            }
            var userApi = socket.GetService<IUserApi>();
            var reference = $"refund-{refund.ProductId}-{socket.SessionInfo.ConnectionId.Truncate(4)}";
            var refundevent = await userApi.UserUserIdTransactionIdDeleteAsync(socket.UserId, int.Parse(refund.Id));
            socket.Dialog(db => db.MsgLine($"Refunded {McColorCodes.AQUA}{refundevent.Amount} coins"));
        }
        if (command == "use")
        {
            await SwitchAccountInUse(socket, args);
            return;
        }
        if (command == "useconfig")
        {
            await SwitchConfigInUse(socket, args);
            return;
        }
        if (command == "refresh")
        {
            socket.Dialog(db => db.MsgLine("Refreshing all licenses (expire time)"));
            await RefreshLicenseTime(socket);
            socket.Dialog(db => db.MsgLine("Refreshed all licenses"));
            return;
        }
        await Help(socket, stringArgs);
    }

    private static async Task RefreshLicenseTime(MinecraftSocket socket)
    {
        var all = await GetCurrentLicenses(socket);
        foreach (var item in all.Licenses)
        {
            var virtualId = $"{socket.UserId}#{item.VirtualId}";
            var userTier = await socket.GetService<PremiumService>().GetCurrentTier(virtualId);
            item.Tier = userTier.Item1 ?? AccountTier.PREMIUM;
            item.Expires = userTier.Item2;
        }
        await socket.GetService<SettingsService>().UpdateSetting(socket.UserId, "licenses", all);
    }

    private async Task SwitchConfigInUse(MinecraftSocket socket, string[] args)
    {
        if (args.Length < 3)
        {
            socket.Dialog(db => db.MsgLine($"Usage: {McColorCodes.AQUA}/cofl licenses useconfig {McColorCodes.GREEN}<id> {McColorCodes.GOLD}<configName>")
                .MsgLine($"Where {McColorCodes.GREEN}<id> {McColorCodes.GRAY}is the first number or user name from {McColorCodes.AQUA}/cofl licenses list{McColorCodes.GRAY}")
                .MsgLine($"and {McColorCodes.GOLD}<configName> {McColorCodes.GRAY}is the name of the config you want to use from {McColorCodes.AQUA}/cl ownconfigs"));
            return;
        }
        var id = args[1];
        var configName = args[2];
        var licenses = await GetList(socket);
        if (!int.TryParse(id, out var virtualId))
        {
            var matchingName = licenses.FirstOrDefault(l => l.TargetName?.Equals(id, StringComparison.InvariantCultureIgnoreCase) ?? false);
            if (matchingName != null)
            {
                virtualId = matchingName.VirtualId;
            }
            else
            {
                socket.Dialog(db => db.MsgLine("Use requires the license id (first character of /cofl licenses list) and the config name"));
                return;
            }
        }
        var settings = await GetCurrentLicenses(socket);
        var targetLicense = settings.Licenses.FirstOrDefault(l => l.VirtualId == virtualId);
        if (targetLicense == null)
        {
            socket.Dialog(db => db.MsgLine($"No license with id {id} found"));
            return;
        }
        var ownConfigs = await OwnConfigsCommand.GetOwnConfigs(socket);
        if (configName == "reset")
        {
            configName = null;
        }
        else if (configName.StartsWith("backup:"))
        {
            var backupName = configName.Substring(7);
            var backups = await BackupCommand.GetBackupList(socket);
            var backup = backups.FirstOrDefault(b => b.Name.Equals(backupName, StringComparison.InvariantCultureIgnoreCase));
            if (backup == null)
            {
                socket.Dialog(db => db.MsgLine($"No backup with name {backupName} found"));
                return;
            }
        }
        else
        {
            var ownConfig = ownConfigs.FirstOrDefault(c => c.Name.Equals(configName, StringComparison.InvariantCultureIgnoreCase));
            if (ownConfig == null)
            {
                socket.Dialog(db => db.MsgLine($"No config with name {configName} found"));
                return;
            }
        }
        targetLicense.ConfigUsed = configName;
        await socket.GetService<SettingsService>().UpdateSetting(socket.UserId, "licenses", settings);
        await socket.sessionLifesycle.FilterState.SubToConfigChanges(); // request config update
        if (configName == null)
        {
            socket.Dialog(db => db.MsgLine($"Removed custom config settings for license {McColorCodes.AQUA}{id}"));
            return;
        }
        socket.Dialog(db => db.MsgLine($"Switched license {McColorCodes.AQUA}{id}{McColorCodes.RESET} to use config {McColorCodes.GOLD}{configName}"));
    }

    private static async Task SwitchAccountInUse(MinecraftSocket socket, string[] args)
    {
        if (args.Length < 3)
        {
            socket.Dialog(db => db.MsgLine("Please provide the license id (first character of /cofl licenses list) and the username to switch to"));
            return;
        }
        var id = args[1];
        var userName = args[2];
        if (!int.TryParse(id, out var virtualId))
        {
            socket.Dialog(db => db.MsgLine("Use requires the license id (first character of /cofl licenses list) and the username"));
            return;
        }
        var settings = await GetCurrentLicenses(socket);
        var license = settings.Licenses.FirstOrDefault(l => l.VirtualId == virtualId);
        if (license == null)
        {
            socket.Dialog(db => db.MsgLine($"No license with id {id} found"));
            return;
        }
        var uuid = await socket.GetPlayerUuid(userName);
        if (uuid == null)
        {
            socket.Dialog(db => db.MsgLine($"No player with name {userName} found"));
            return;
        }
        license.UseOnAccount = uuid;
        await socket.GetService<SettingsService>().UpdateSetting(socket.UserId, "licenses", settings);
        socket.Dialog(db => db.MsgLine($"Switched license {id} to {userName}"));
    }

    protected virtual Task Help(MinecraftSocket socket, string subArgs)
    {
        socket.Dialog(db => db
            .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} add <userName>{DEFAULT_COLOR} request a new license")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} use <id> <userName>{DEFAULT_COLOR} switch the user of a license")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} useconfig <id> <config>{DEFAULT_COLOR} use a certain config", null, "with backup:name you can select configs from your backup")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists all licenses")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} default <userName>{DEFAULT_COLOR} switch mcName using account premium")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} refresh{DEFAULT_COLOR} refresh all licenses (time andtier)")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} help{DEFAULT_COLOR} display this help"));
        return Task.CompletedTask;
    }

    protected override async Task Add(MinecraftSocket socket, string subArgs)
    {
        var subargs = subArgs.Split(' ');
        var name = subargs[0];
        if (string.IsNullOrWhiteSpace(name))
        {
            var uuids = await socket.sessionLifesycle.GetMinecraftAccountUuids();
            await PrintLicenseOptions(socket, uuids, "Select the account you want to purchase a license for");
            return;
        }
        if (subargs.Length == 3 && subargs[2] == socket.SessionInfo.ConnectionId)
        {
            var uuid = await socket.GetPlayerUuid(name);
            var licenseApi = socket.GetService<ILicenseApi>();
            var userApi = socket.GetService<IUserApi>();
            var productApi = socket.GetService<IProductsApi>();
            LicenseSetting settings = await GetCurrentLicenses(socket);
            var usedLicense = settings.Licenses.FirstOrDefault(l => l.UseOnAccount == uuid);
            if (usedLicense != null)
            {
                socket.Dialog(db => db.MsgLine($"Extending license for {name} {McColorCodes.GRAY}({uuid})"));
            }
            else
            {
                socket.Dialog(db => db.MsgLine($"Adding license for {name}"));
                usedLicense = new LicenseInfo
                {
                    UseOnAccount = uuid,
                    VirtualId = settings.Licenses.Count + 1
                };
                settings.Licenses.Add(usedLicense);

                await socket.GetService<SettingsService>().UpdateSetting(socket.UserId, "licenses", settings);
            }
            var product = await productApi.ProductsPProductSlugGetAsync(subargs[1]);
            try
            {
                var virtualuser = $"{socket.UserId}#{usedLicense.VirtualId}";
                var reference = $"license-{usedLicense.VirtualId}-{subargs[1]}-{socket.SessionInfo.ConnectionId.Truncate(4)}";
                await userApi.UserUserIdTransferPostAsync(socket.UserId, new TransferRequest
                {
                    Amount = product.Cost,
                    Reference = reference,
                    TargetUser = virtualuser
                });
                var previousExpire = await socket.GetService<PremiumService>().GetCurrentTier(virtualuser);
                await DoOrRetryPurchase(subargs, userApi, virtualuser, reference);
                for (int i = 0; i < 10; i++)
                {
                    if (usedLicense.Expires > previousExpire.Item2)
                        break;
                    await Task.Delay(2000); // wait for transaction to be processed
                    var userTier = await socket.GetService<PremiumService>().GetCurrentTier(virtualuser);
                    usedLicense.Tier = userTier.Item1 ?? AccountTier.PREMIUM;
                    usedLicense.Expires = userTier.Item2;
                }
                await socket.GetService<SettingsService>().UpdateSetting(socket.UserId, "licenses", settings);
            }
            catch (Payments.Client.Client.ApiException e)
            {
                var message = e.Message.Substring(68).Trim('}', '"');
                socket.Dialog(db => db.MsgLine(McColorCodes.RED + "An error occured").Msg(message)
                    .If(() => e.Message.Contains("insuficcient balance"), db => db.CoflCommand<TopUpCommand>(McColorCodes.AQUA + "Click here to top up coins", "", "Click here to buy coins"))
                    .If(() => e.Message.Contains("same reference found"), db =>
                        db.MsgLine(
                            McColorCodes.AQUA + "To prevent accidental loss of coins you can only purchase once per connection.", null,
                            "You can buy licenses for different accounts tho")));
            }
            socket.Dialog(db => db.MsgLine($"Successfully requested a license for {name}"));
            if (name == socket.SessionInfo.McName)
            {
                await Task.Delay(5000); // cache refresh
                await socket.sessionLifesycle.TierManager.RefreshTier();
                var tiername = await socket.sessionLifesycle.TierManager.GetCurrentCached();
                socket.Dialog(db => db.MsgLine($"This connection is now {McColorCodes.AQUA}{tiername} {McColorCodes.RESET} until {usedLicense.Expires}"));
            }

            return;
        }
        if (subargs.Length == 2 && subargs[1].StartsWith("prem"))
        {
            socket.Dialog(db => db.MsgLine($"Click to confirm purchase/extend a license for {McColorCodes.AQUA}{name}", null, "click on the tier name below")
                .If(() => !subargs[1].Contains("plus"), db => db.CoflCommand<LicensesCommand>($"  {McColorCodes.GREEN}Premium  ", $"add {name} premium {socket.SessionInfo.ConnectionId}", "Purchase premium license"))
                .If(() => subargs[1] == "premium_plus-week", db => db.CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}Premium+ {McColorCodes.GRAY}1week ", $"add {name} premium_plus-week {socket.SessionInfo.ConnectionId}", "Purchase premium+ license"))
                .If(() => subargs[1] == "premium_plus-weeks", db => db.CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}Premium+ 4 weeks ", $"add {name} premium_plus-weeks {socket.SessionInfo.ConnectionId}", "Purchase premium+ license for 4 weeks"))
                .If(() => subargs[1] == "premium_plus-months", db => db.CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}Premium+ 11 weeks ", $"add {name} premium_plus-months {socket.SessionInfo.ConnectionId}", "Purchase premium+ license for 11 weeks")));
            return;
        }
        socket.Dialog(db => db.MsgLine("Which tier do you want to purchase/extend")
            .CoflCommand<LicensesCommand>($"  {McColorCodes.GREEN}Premium  ", $"add {name} premium {socket.SessionInfo.ConnectionId}", "Purchase/extend premium license")
            .CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}Premium+ 1 ", $"add {name} premium_plus-week {socket.SessionInfo.ConnectionId}", "Purchase/extend premium+ license\nfor 1 week")
            .CoflCommand<LicensesCommand>($" {McColorCodes.GOLD}{McColorCodes.ITALIC}/ 4 weeks  ", $"add {name} premium_plus-weeks {socket.SessionInfo.ConnectionId}", "Purchase/extend premium+ license\nfor 4 weeks")
            .CoflCommand<LicensesCommand>($" {McColorCodes.GOLD}{McColorCodes.ITALIC}/ 11 weeks  ", $"add {name} premium_plus-months {socket.SessionInfo.ConnectionId}", "Purchase/extend premium+ license\nfor 11 weeks"));
    }

    private static async Task DoOrRetryPurchase(string[] subargs, IUserApi userApi, string virtualuser, string reference)
    {
        try
        {
            await userApi.UserUserIdServicePurchaseProductSlugPostAsync(virtualuser, subargs[1], reference);
        }
        catch (Payments.Client.Client.ApiException e)
        {
            if (!e.Message.Contains("same reference found"))
            {
                await Task.Delay(5000);
                try
                {
                    await userApi.UserUserIdServicePurchaseProductSlugPostAsync(virtualuser, subargs[1], reference);
                }
                catch (Exception ei)
                {
                    if (!ei.Message.Contains("same reference found"))
                        throw;
                }
            }
        }
    }

    private static async Task<LicenseSetting> GetCurrentLicenses(MinecraftSocket socket)
    {
        return await socket.GetService<SettingsService>().GetCurrentValue<LicenseSetting>(socket.UserId, "licenses", () => new LicenseSetting());
    }

    protected override async Task NoEntriesFound(MinecraftSocket socket, string subArgs)
    {
        var uuids = await socket.sessionLifesycle.GetMinecraftAccountUuids();
        if (uuids.Count() <= 1)
        {
            socket.Dialog(db => db.MsgLine("You only have one account, you may want to use").CoflCommand<PurchaseCommand>($"{McColorCodes.AQUA} /cofl buy", "", "Get buy menu"));
            return;
        }
        await PrintLicenseOptions(socket, uuids, "You don't have any licenses yet. You can purchase one for one of your verified accounts");
    }

    private static async Task PrintLicenseOptions(MinecraftSocket socket, IEnumerable<string> uuids, string heading)
    {
        var allnames = await GetNames(socket, uuids);
        socket.Dialog(db => db.MsgLine(heading)
            .ForEach(uuids, (db, id) => db.Msg($"{McColorCodes.GRAY}> {McColorCodes.AQUA}{allnames.GetValueOrDefault(id) ?? id}")
                .CoflCommand<LicensesCommand>($" {McColorCodes.GREEN}premium ", $"add {allnames.GetValueOrDefault(id) ?? id} premium", $"Purchase/extend premium license for {allnames.GetValueOrDefault(id) ?? id}")
                .CoflCommand<LicensesCommand>($" {McColorCodes.GOLD}Premium+ ", $"add {allnames.GetValueOrDefault(id) ?? id} premium_plus-week", $"Purchase/extend premium+ license for {allnames.GetValueOrDefault(id) ?? id}")
                .CoflCommand<LicensesCommand>($" {McColorCodes.GOLD}{McColorCodes.ITALIC}4 weeks ", $"add {allnames.GetValueOrDefault(id) ?? id} premium_plus-weeks", $"Purchase/extend premium+ license for {allnames.GetValueOrDefault(id) ?? id}\nfor 4 weeks (16% discount)")
                .CoflCommand<LicensesCommand>($" {McColorCodes.GOLD}{McColorCodes.BOLD}11 weeks ", $"add {allnames.GetValueOrDefault(id) ?? id} premium_plus-months", $"Purchase/extend premium+ license for {allnames.GetValueOrDefault(id) ?? id}\nfor 11 weeks (27% discount)")
                .LineBreak()));
    }

    protected override async Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
    {
        return null;
    }

    protected override string Format(PublicLicenseWithName elem)
    {
        if (elem.Expires < DateTime.UtcNow)
        {
            return $"{McColorCodes.GRAY}> {McColorCodes.GREEN}{elem.TargetName} {McColorCodes.DARK_GREEN}{McColorCodes.STRIKE}{elem.Tier}{McColorCodes.RED} expired";
        }
        return $"{McColorCodes.GRAY}{elem.VirtualId}> {McColorCodes.GREEN}{elem.TargetName} {McColorCodes.DARK_GREEN}{elem.Tier} {McColorCodes.AQUA}{FormatTime(elem)}{McColorCodes.GRAY}";
    }

    private static string FormatTime(PublicLicenseWithName elem)
    {
        return FormatProvider.FormatTimeGlobal(elem.Expires - DateTime.UtcNow);
    }

    protected override void ListResponse(DialogBuilder d, PublicLicenseWithName e)
    {
        var displayText = $" {McColorCodes.YELLOW}[EXTEND]{DEFAULT_COLOR}";
        var hoverText = $"Extend {LongFormat(e)}";
        if (e.Expires < DateTime.UtcNow)
        {
            displayText = $" {McColorCodes.GREEN}[RENEW]{DEFAULT_COLOR}";
            hoverText = $"Renew {McColorCodes.DARK_GREEN}{e.Tier} {McColorCodes.GRAY}for {McColorCodes.GREEN}{e.TargetName}";
        }
        FormatForList(d, e).MsgLine(displayText, $"/cofl {Slug} add {e.UseOnAccount} {e.Tier}", hoverText);
    }

    protected override string GetId(PublicLicenseWithName elem)
    {
        return elem.UseOnAccount + elem.Tier;
    }

    protected override async Task<List<PublicLicenseWithName>> GetList(MinecraftSocket socket)
    {
        var settingsTask = socket.GetService<SettingsService>().GetCurrentValue<LicenseSetting>(socket.UserId, "licenses", () => new LicenseSetting());
        var settings = await settingsTask;
        Dictionary<string, string> allnames = await GetNames(socket, settings.Licenses.Select(l => l.UseOnAccount));
        return settings.Licenses.Select(l => new PublicLicenseWithName
        {
            Expires = l.Expires,
            Tier = l.Tier,
            UseOnAccount = l.UseOnAccount,
            VirtualId = l.VirtualId,
            TargetName = allnames?.GetValueOrDefault(l.UseOnAccount) ?? l.UseOnAccount.Truncate(5) + "..."
        }).ToList();
    }

    protected override async Task List(MinecraftSocket socket, string subArgs)
    {
        var nameTask = NewMethod(socket);
        await base.List(socket, subArgs);
        var name = await nameTask;
        if (name != null)
        {
            var message = $"The default ign is the minecraft account\n"
                + $"you want to use your (email)account tier on.\n"
                + $"That is the tier you buy with /cofl buy or on\n"
                + $"the website. Different to licenses you can switch";
            if (name != socket.SessionInfo.McName)
                message += $"\n{McColorCodes.GRAY}Click to change to your current account";
            socket.Dialog(db => db.CoflCommand<LicensesCommand>($"Your default account is {McColorCodes.AQUA}{name}", "default " + name, message));
        }
        var currentId = socket.SessionInfo.McUuid;
        var defaultAccount = socket.sessionLifesycle.TierManager.DefaultAccount;
        var licenses = await GetList(socket);
        var allIds = licenses.Where(l => l.Expires > DateTime.UtcNow).Select(l => l.UseOnAccount).ToList();
        if (defaultAccount != null)
            allIds.Add(defaultAccount);
        if (!allIds.Contains(currentId))
        {
            socket.Dialog(db => db.MsgLine($"You don't have a license for {McColorCodes.AQUA}{socket.SessionInfo.McName}")
                .MsgLine($"You can buy one with {McColorCodes.AQUA}/cofl license add {socket.SessionInfo.McName}", $"/cofl license add {socket.SessionInfo.McName}", "Click to buy a license")
                .MsgLine($"Use your account tier {McColorCodes.AQUA}/cofl license default {socket.SessionInfo.McName}", $"/cofl license default {socket.SessionInfo.McName}", "Click to set default account")
                .If(() => licenses.Count >= 1, db => db.MsgLine($"Switch license 1 with {McColorCodes.AQUA}/cl license use 1 {socket.SessionInfo.McName}", $"/cofl license use 1 {socket.SessionInfo.McName}", "Click to switch license 1")));
            return;
        }
        // to avoid desyncing issues from the payment system refresh it every time
        await RefreshLicenseTime(socket);


        static async Task<string> NewMethod(MinecraftSocket socket)
        {
            return await socket.GetPlayerName(socket.sessionLifesycle.TierManager.DefaultAccount);
        }
    }

    private static async Task<Dictionary<string, string>> GetNames(MinecraftSocket socket, IEnumerable<string> uuids)
    {
        var nameApi = socket.GetService<PlayerName.PlayerNameService>();
        var allnames = await nameApi.GetNames(uuids);
        return allnames;
    }

    protected override Task Update(MinecraftSocket socket, List<PublicLicenseWithName> newCol)
    {
        // ignore
        return Task.CompletedTask;
    }
}

public class PublicLicenseWithName : LicenseInfo
{
    public string TargetName { get; set; }
}

public class LicenseInfo
{
    public string UseOnAccount { get; set; }
    public DateTime Expires { get; set; }
    public int VirtualId { get; set; }
    public AccountTier Tier { get; set; }
    public string ConfigUsed { get; set; }
}

public class LicenseSetting
{
    public List<LicenseInfo> Licenses { get; set; }
    public LicenseSetting()
    {
        Licenses = new List<LicenseInfo>();
    }
}