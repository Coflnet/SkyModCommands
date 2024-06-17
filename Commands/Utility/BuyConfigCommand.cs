using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class BuyConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<sellerIgn> <configName> [confirmId=none]";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var seller = args["sellerIgn"];
        var name = args["configName"];
        var configs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        var key = SellConfigCommand.GetKeyFromname(name);
        var sellerUserId = await GetUserIdFromMcName(socket, seller);
        var toBebought = await SelfUpdatingValue<ConfigContainer>.Create(sellerUserId, key, () => null);
        var reference = $"{name} config from {seller}";
        if (toBebought.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        if (configs.Value.Configs.Any(c => c.Name == name && c.OwnerId == sellerUserId))
        {
            socket.SendMessage("You already own this config.");
            return;
        }
        if (args["confirmId"] != socket.SessionInfo.SessionId)
        {
            Console.WriteLine("confirming: " + args["confirmId"]);
            socket.Dialog(db => db.CoflCommand<BuyConfigCommand>($"Confirm buying §6{toBebought.Value.Name} §7v{toBebought.Value.Version} for §6{toBebought.Value.Price} CoflCoins {McColorCodes.YELLOW}[CLICK]",
                $"{seller} {name} {socket.SessionInfo.SessionId}",
                $"§aBuy {toBebought.Value.Name} from {seller} for {toBebought.Value.Price} CoflCoins?"));
            // check if it is already bought
            await CheckIncompletePurchase(socket, seller, name, configs, sellerUserId, toBebought, reference);
            return;
        }
        if (toBebought.Value.Price != 0)
        {
            try
            {
                var userInfo = await socket.GetService<IUserApi>().UserUserIdServicePurchaseProductSlugPostAsync(socket.UserId, "config-purchase", reference, toBebought.Value.Price / 600);
            }
            catch (Payments.Client.Client.ApiException e)
            {
                var message = e.Message.Substring(68).Trim('}', '"');
                if (!e.Message.Contains("same reference found"))
                { // if same reference exists but the own check did not succeed we probably didn't credit the person the config so do that now
                    socket.Dialog(db => db.MsgLine(McColorCodes.RED + "An error occured").Msg(message)
                        .If(() => e.Message.Contains("insuficcient balance"), db => db.CoflCommand<TopUpCommand>(McColorCodes.AQUA + "Click here to top up coins", "", "Click here to buy coins")));
                    return;
                }
            }
        }
        await FinishPurchase(socket, seller, name, configs, sellerUserId, toBebought);

    }

    private static async Task CheckIncompletePurchase(IMinecraftSocket socket, string seller, string name, SelfUpdatingValue<OwnedConfigs> configs, string sellerUserId, SelfUpdatingValue<ConfigContainer> toBebought, string reference)
    {
        var recentTransactions = await socket.GetService<ITransactionApi>().TransactionUUserIdGetAsync(socket.UserId);
        if (recentTransactions.Any(t => t.Reference == reference))
        {
            socket.SendMessage("Don't mind that, you already bought this config. Making it available for you in /cofl ownconfigs by retrying successful purchase.");
            await FinishPurchase(socket, seller, name, configs, sellerUserId, toBebought);
            return;
        }
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
        try
        {

            var topupApi = socket.GetService<ITopUpApi>();
            socket.Dialog(db => db.MsgLine($"§6{toBebought.Value.Name} §7v{toBebought.Value.Version} §6bought"));
            if (toBebought.Value.Price != 0)
                await topupApi.TopUpCustomPostAsync(sellerUserId, new()
                {
                    Amount = toBebought.Value.Price * 70 / 100,
                    Reference = $"config sale {name} to {socket.SessionInfo.McName}",
                    ProductId = "config-sell"
                });
        }
        catch (Exception e)
        {
            socket.Error(e, "Failed to credit the seller");
        }
        socket.ExecuteCommand($"/cofl loadconfig {sellerUserId} {name}");
    }
}
