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
        if (command == "slots")
        {
            var client = socket.GetService<ITierSlotsApi>();
            var owned = await client.ApiTierSlotsOwnerOwnerIdGetAsync(socket.UserId);
            if (args.Length == 1)
            {
                socket.Dialog(db => db.MsgLine("Your tier slots:"));
                foreach (var slot in owned)
                    socket.Dialog(db => db.MsgLine($"{slot.Id}: {slot.Tier} until {slot.Expires:u}, assigned to {slot.MinecraftUuid ?? slot.AssignedUserId ?? "nobody"}"));
                var assigned = await client.ApiTierSlotsAccessGetAsync(socket.UserId, socket.SessionInfo.McUuid);
                foreach (var slot in assigned.Where(s => !s.CanManage))
                    socket.Dialog(db => db.MsgLine($"{slot.Tier} until {slot.Expires:u}: owned and managed by another account"));
                socket.Dialog(db => db.MsgLine("Use /cofl licenses slots <id> <Minecraft name|clear> to assign or release an owned slot."));
                return;
            }
            if (args.Length != 3 || !long.TryParse(args[1], out var slotId))
                throw new CoflnetException("invalid_arguments", "Use /cofl licenses slots <id> <Minecraft name|clear>");
            var target = owned.FirstOrDefault(s => s.Id == slotId)
                ?? throw new CoflnetException("not_owner", "Only the purchaser can manage this slot");
            var minecraftUuid = args[2] == "clear" ? null : await socket.GetPlayerUuid(args[2]);
            if (args[2] != "clear" && string.IsNullOrEmpty(minecraftUuid))
                throw new CoflnetException("unknown_player", "Minecraft account not found");
            await client.ApiTierSlotsOwnerOwnerIdIdAssignmentPutAsync(socket.UserId, slotId,
                new TierSlotAssignment(minecraftUuid: minecraftUuid, varVersion: target.VarVersion));
            await socket.sessionLifesycle.TierManager.RefreshTier();
            socket.Dialog(db => db.MsgLine("Slot assignment updated"));
            return;
        }
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

    protected override Task Help(MinecraftSocket socket, string subArgs)
    {
        socket.Dialog(db => db
            .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} add <userName>{DEFAULT_COLOR} buy or extend a tier slot for an account")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} use <id> <userName>{DEFAULT_COLOR} switch the user of a license")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} useconfig <id> <config>{DEFAULT_COLOR} use a certain config", null, "with backup:name you can select configs from your backup")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists all licenses")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} slots{DEFAULT_COLOR} manage purchased tier slots")
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
            await PrintLicenseOptions(socket, uuids, "Select the account you want to purchase a tier slot for");
            return;
        }

        if (name.Length <= 3 && int.TryParse(name, out var id))
            name = await GetLegacyLicenseAccountName(socket, id) ?? name;

        var products = await socket.GetService<ITierSlotsApi>().ApiTierSlotsProductsGetAsync();
        var product = subargs.Length >= 2 ? products.FirstOrDefault(p => p.Slug == subargs[1]) : null;
        if (product != null && subargs.Length == 3 && subargs[2] == socket.SessionInfo.ConnectionId)
        {
            await PurchaseSlot(socket, name, product);
            return;
        }
        if (product != null)
        {
            socket.Dialog(db => db.MsgLine($"Click to confirm purchase/extend a tier slot for {McColorCodes.AQUA}{name}", null, "click on the package below")
                .CoflCommand<LicensesCommand>(FormatProduct(socket, product), $"add {name} {product.Slug} {socket.SessionInfo.ConnectionId}", ProductHover(product)));
            return;
        }
        socket.Dialog(db => db.MsgLine($"Which tier slot do you want to purchase/extend for {McColorCodes.AQUA}{name}")
            .ForEach(products, (db, p) => db.CoflCommand<LicensesCommand>(FormatProduct(socket, p), $"add {name} {p.Slug} {socket.SessionInfo.ConnectionId}", ProductHover(p))));
    }

    private static async Task<string> GetLegacyLicenseAccountName(MinecraftSocket socket, int virtualId)
    {
        var settings = await GetCurrentLicenses(socket);
        var license = settings.Licenses.FirstOrDefault(l => l.VirtualId == virtualId);
        if (license == null)
            return null;
        var names = await GetNames(socket, new[] { license.UseOnAccount });
        return names.GetValueOrDefault(license.UseOnAccount);
    }

    /// <summary>
    /// Buys a slot package for the account owner and makes it usable on the named Minecraft account.
    /// A single-slot package extends the owner's slot of the same tier already assigned to that account.
    /// </summary>
    private static async Task PurchaseSlot(MinecraftSocket socket, string name, PurchaseableProduct product)
    {
        var uuid = await socket.GetPlayerUuid(name);
        if (string.IsNullOrEmpty(uuid))
            throw new CoflnetException("unknown_player", "Minecraft account not found");
        var slotsApi = socket.GetService<ITierSlotsApi>();
        var userApi = socket.GetService<IUserApi>();
        var owned = await slotsApi.ApiTierSlotsOwnerOwnerIdGetAsync(socket.UserId);
        var reference = $"slot-{uuid.Truncate(8)}-{product.Slug}-{socket.SessionInfo.ConnectionId.Truncate(4)}";
        var existing = product.SlotCount == 1
            ? owned.Where(s => s.Tier == product.SlotTier && IsSameAccount(s.MinecraftUuid, uuid)).MaxBy(s => s.Expires)
            : null;
        try
        {
            if (existing != null)
            {
                socket.Dialog(db => db.MsgLine($"Extending tier slot {existing.Id} for {name} {McColorCodes.GRAY}({uuid})"));
                var request = new ServicePurchaseRequest(reference: reference, count: 1, slotIds: [existing.Id],
                    locale: "en", requestId: Guid.NewGuid().ToString());
                await DoOrRetryPurchase(() => userApi.UserUserIdServicePurchaseDeclaredProductSlugPostAsync(socket.UserId, product.Slug, request));
            }
            else
            {
                socket.Dialog(db => db.MsgLine($"Adding tier slot for {name}"));
                await DoOrRetryPurchase(() => userApi.UserUserIdServicePurchaseProductSlugPostAsync(socket.UserId, product.Slug, reference, 1));
                await AssignNewSlot(socket, slotsApi, owned, product, uuid);
            }
        }
        catch (Payments.Client.Client.ApiException e)
        {
            ShowPurchaseError(socket, e);
            return;
        }
        socket.Dialog(db => db.MsgLine($"Successfully purchased a {product.SlotTier} slot for {name}"));
        if (IsSameAccount(socket.SessionInfo.McUuid, uuid))
        {
            await socket.sessionLifesycle.TierManager.RefreshTier();
            var tiername = await socket.sessionLifesycle.TierManager.GetCurrentCached();
            socket.Dialog(db => db.MsgLine($"This connection is now {McColorCodes.AQUA}{tiername}"));
        }
    }

    private static async Task AssignNewSlot(MinecraftSocket socket, ITierSlotsApi slotsApi, List<TierSlotAccess> before, PurchaseableProduct product, string uuid)
    {
        var after = await slotsApi.ApiTierSlotsOwnerOwnerIdGetAsync(socket.UserId);
        var created = after.FirstOrDefault(s => s.Tier == product.SlotTier && s.MinecraftUuid == null && s.AssignedUserId == null
            && !before.Any(b => b.Id == s.Id));
        if (created == null)
        {
            socket.Dialog(db => db.MsgLine($"Assign your new slot with {McColorCodes.AQUA}/cofl licenses slots"));
            return;
        }
        await slotsApi.ApiTierSlotsOwnerOwnerIdIdAssignmentPutAsync(socket.UserId, created.Id,
            new TierSlotAssignment(minecraftUuid: uuid, varVersion: created.VarVersion));
    }

    private static bool IsSameAccount(string a, string b)
    {
        return a != null && b != null && a.Replace("-", "").Equals(b.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
    }

    private static void ShowPurchaseError(MinecraftSocket socket, Payments.Client.Client.ApiException e)
    {
        var message = e.Message.Substring(Math.Min(68, e.Message.Length)).Trim('}', '"');
        socket.Dialog(db => db.MsgLine(McColorCodes.RED + "An error occured").Msg(message)
            .If(() => e.Message.Contains("insuficcient balance"), db => db.CoflCommand<TopUpCommand>(McColorCodes.AQUA + "Click here to top up coins", "", "Click here to buy coins"))
            .If(() => e.Message.Contains("same reference found"), db =>
                db.MsgLine(
                    McColorCodes.AQUA + "To prevent accidental loss of coins you can only purchase once per connection.", null,
                    "You can buy slots for different accounts tho")));
    }

    private static string FormatProduct(MinecraftSocket socket, PurchaseableProduct product)
    {
        return $" {McColorCodes.GOLD}{product.Title} {McColorCodes.GRAY}{socket.FormatPrice(product.Cost)} ";
    }

    private static string ProductHover(PurchaseableProduct product)
    {
        var slots = product.SlotCount == 1 ? "1 slot" : $"{product.SlotCount} slots";
        return $"Purchase/extend {product.SlotTier} for {FormatProvider.FormatTimeGlobal(TimeSpan.FromSeconds(product.OwnershipSeconds))}\n{slots}, {product.Description}";
    }

    private static async Task DoOrRetryPurchase(Func<Task> purchase)
    {
        try
        {
            await purchase();
        }
        catch (Payments.Client.Client.ApiException e)
        {
            if (!e.Message.Contains("same reference found"))
            {
                await Task.Delay(5000);
                try
                {
                    await purchase();
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
        var products = await socket.GetService<ITierSlotsApi>().ApiTierSlotsProductsGetAsync();
        socket.Dialog(db => db.MsgLine(heading)
            .ForEach(uuids, (db, id) => db.Msg($"{McColorCodes.GRAY}> {McColorCodes.AQUA}{allnames.GetValueOrDefault(id) ?? id}")
                .ForEach(products, (db, p) => db.CoflCommand<LicensesCommand>(FormatProduct(socket, p), $"add {allnames.GetValueOrDefault(id) ?? id} {p.Slug}", ProductHover(p)))
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