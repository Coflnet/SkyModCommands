using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class BuyConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<sellerIgn> <configName> [confirmId=none]";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var seller = args["sellerIgn"];
        var name = args["configName"];
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        var key = SellConfigCommand.GetKeyFromname(name);
        var sellerUserId = await GetUserIdFromMcName(socket, seller);
        using var toBebought = await SelfUpdatingValue<ConfigContainer>.Create(sellerUserId, key, () => null);
        if (toBebought.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        if (configs.Value.Configs.Any(c => c.Name == name && c.OwnerId == sellerUserId))
        {
            socket.Dialog(db => db.CoflCommand<LoadConfigCommand>(
                $"You already own this config. {McColorCodes.YELLOW}[CLICK to load]",
                $"{sellerUserId} {name}",
                $"Click here to load the config\n{McColorCodes.AQUA}/cofl loadconfig {sellerUserId} {name}"));
            return;
        }
        if (toBebought.Value.Price != 0)
        {
            socket.Dialog(db => db
                .MsgLine($"{McColorCodes.YELLOW}Paid config purchases are temporarily unavailable.")
                .Msg($"Payment for {toBebought.Value.Name} can't currently be processed through Coflnet.")
                .Msg($"Please contact {seller} directly and politely ask whether they are willing to gift you the config for free.")
                .Msg($"{McColorCodes.GRAY}Do not send payment or CoflCoins. Your balance was not charged. The creator can use /cofl giftconfig {name} {socket.SessionInfo.McName}; supported updates will keep working."));
            return;
        }
        if (args["confirmId"] != socket.SessionInfo.SessionId)
        {
            var summary = $"This config has {toBebought.Value.Settings.WhiteList.Count} whitelist entries and {toBebought.Value.Settings.BlackList.Count} blacklist entries.\n"
                + $"It was last updated {McColorCodes.GREEN}{socket.formatProvider.FormatTime(DateTime.Now - toBebought.Value.Diffs.LastOrDefault().Value.CreatedAt)} ago{McColorCodes.RESET}. It had {McColorCodes.AQUA}{toBebought.Value.Version}{McColorCodes.RESET} updates and has the following change notes:\n{McColorCodes.GRAY}{toBebought.Value.ChangeNotes}";
            socket.Dialog(db => db
                .MsgLine("This is a managed Coflnet config. You can edit it and create in-service backups, but Expert-provided settings can't be exported or redistributed.")
                .MsgLine($"{McColorCodes.GRAY}Your own additions and overrides can be exported separately. No CoflCoins will be charged.")
                .CoflCommand<BuyConfigCommand>($"Confirm adding free config §6{toBebought.Value.Name} §7v{toBebought.Value.Version} {McColorCodes.YELLOW}[CLICK]",
                    $"{seller} {name} {socket.SessionInfo.SessionId}",
                    $"§aAdd {toBebought.Value.Name} for managed use in Coflnet?"
                    + $"\n{summary}"));
            return;
        }
        await FinishPurchase(socket, seller, name, configs, sellerUserId, toBebought);

    }

    private static async Task FinishPurchase(IMinecraftSocket socket, string seller, string name, SelfUpdatingValue<OwnedConfigs> configs, string sellerUserId, SelfUpdatingValue<ConfigContainer> toBebought)
    {
        configs.Value.Configs.Add(new OwnedConfigs.OwnedConfig()
        {
            Name = name,
            Version = toBebought.Value.Version,
            ChangeNotes = toBebought.Value.ChangeNotes,
            OwnerId = sellerUserId,
            PricePaid = toBebought.Value.Price,
            OwnerName = seller
        });
        await configs.Update();
        socket.Dialog(db => db.MsgLine($"Free config §6{toBebought.Value.Name} §7v{toBebought.Value.Version} §fadded"));
        socket.ExecuteCommand($"/cofl loadconfig {sellerUserId} {name}");
    }
}
