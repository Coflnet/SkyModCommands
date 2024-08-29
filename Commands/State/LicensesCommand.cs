using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

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
            await socket.sessionLifesycle.TierManager.ChangeDefaultTo(uuid);
            socket.Dialog(db => db.MsgLine($"Changed default account you use your account premium on to {McColorCodes.AQUA}{args[1]}"));
            return;
        }
        await Help(socket, stringArgs);
    }

    protected virtual Task Help(MinecraftSocket socket, string subArgs)
    {
        socket.Dialog(db => db
            .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} add <userName>{DEFAULT_COLOR} request a new license")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists all licenses")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} default <userName>{DEFAULT_COLOR} switch mcName using account premium")
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
            try
            {
                await licenseApi.ApiLicenseUUserIdPProductSlugTTargetIdPostAsync(socket.UserId, subargs[1], uuid, subargs[2]);
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
                await socket.sessionLifesycle.TierManager.RefreshTier();
                var tiername = await socket.sessionLifesycle.TierManager.GetCurrentCached();
                socket.Dialog(db => db.MsgLine($"This connection is now {McColorCodes.AQUA}{tiername}"));
            }

            return;
        }
        if (subargs.Length == 2 && subargs[1].StartsWith("prem"))
        {
            socket.Dialog(db => db.MsgLine("Click to confirm purchase/extend a license for " + name, null, "click on the tier name below")
                .If(() => !subargs[1].Contains("plus"), db => db.CoflCommand<LicensesCommand>($"  {McColorCodes.GREEN}Premium  ", $"add {name} premium {socket.SessionInfo.ConnectionId}", "Purchase premium license"))
                .If(() => subargs[1].Contains("plus"), db => db.CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}Premium+  ", $"add {name} premium_plus {socket.SessionInfo.ConnectionId}", "Purchase premium+ license")));
            return;
        }
        socket.Dialog(db => db.MsgLine("Which tier do you want to purchase/extend")
            .CoflCommand<LicensesCommand>($"  {McColorCodes.GREEN}Premium  ", $"add {name} premium {socket.SessionInfo.ConnectionId}", "Purchase/extend premium license")
            .CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}Premium+  ", $"add {name} premium_plus {socket.SessionInfo.ConnectionId}", "Purchase/extend premium+ license"));
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
                .CoflCommand<LicensesCommand>($"  {McColorCodes.GREEN}{allnames.GetValueOrDefault(id) ?? id}  ", $"add {allnames.GetValueOrDefault(id) ?? id} premium", $"Purchase/extend premium license for {allnames.GetValueOrDefault(id) ?? id}")
                .CoflCommand<LicensesCommand>($"  {McColorCodes.GOLD}{allnames.GetValueOrDefault(id) ?? id}  ", $"add {allnames.GetValueOrDefault(id) ?? id} premium_plus", $"Purchase/extend premium+ license for {allnames.GetValueOrDefault(id) ?? id}").LineBreak()));
    }

    protected override async Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
    {
        return null;
    }

    protected override string Format(PublicLicenseWithName elem)
    {
        if (elem.Expires < DateTime.UtcNow)
        {
            return $"{McColorCodes.GRAY}> {McColorCodes.GREEN}{elem.TargetName} {McColorCodes.DARK_GREEN}{McColorCodes.STRIKE}{elem.ProductSlug}{McColorCodes.RED} expired";
        }
        return $"{McColorCodes.GRAY}> {McColorCodes.GREEN}{elem.TargetName} {McColorCodes.DARK_GREEN}{elem.ProductSlug} {McColorCodes.AQUA}{elem.Expires - DateTime.UtcNow:dd}{McColorCodes.GRAY}days";
    }

    protected override void ListResponse(DialogBuilder d, PublicLicenseWithName e)
    {
        var displayText = $" {McColorCodes.YELLOW}[EXTEND]{DEFAULT_COLOR}";
        var hoverText = $"Extend {LongFormat(e)}";
        if (e.Expires < DateTime.UtcNow)
        {
            displayText = $" {McColorCodes.GREEN}[RENEW]{DEFAULT_COLOR}";
            hoverText = $"Renew {McColorCodes.DARK_GREEN}{e.ProductSlug} {McColorCodes.GRAY}for {McColorCodes.GREEN}{e.TargetName}";
        }
        FormatForList(d, e).MsgLine(displayText, $"/cofl {Slug} add {e.TargetId} {e.ProductSlug}", hoverText);
    }

    protected override string GetId(PublicLicenseWithName elem)
    {
        return elem.TargetId + elem.ProductSlug;
    }

    protected override async Task<List<PublicLicenseWithName>> GetList(MinecraftSocket socket)
    {
        var licenseApi = socket.GetService<ILicenseApi>();
        var licenses = await licenseApi.ApiLicenseUUserIdGetAsync(socket.UserId);
        Dictionary<string, string> allnames = await GetNames(socket, licenses.Select(l => l.TargetId));
        return licenses.ConvertAll(l => new PublicLicenseWithName
        {
            Expires = l.Expires,
            ProductSlug = l.ProductSlug,
            TargetId = l.TargetId,
            TargetName = allnames?.GetValueOrDefault(l.TargetId) ?? l.TargetId.Truncate(5) + "..."
        });
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

public class PublicLicenseWithName : PublicLicense
{
    public string TargetName { get; set; }
}