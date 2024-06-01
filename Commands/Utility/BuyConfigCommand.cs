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
            return;
        }
        if (toBebought.Value.Price != 0 && !await PurchaseCommand.Purchase(socket, socket.GetService<IUserApi>(), "config-purchase", toBebought.Value.Price / 600, $"{name} config from {seller}"))
        {
            socket.Dialog(db => db.MsgLine("Config purchase failed."));
            return;
        }
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
        var topupApi = socket.GetService<ITopUpApi>();
        socket.Dialog(db => db.MsgLine($"§6{toBebought.Value.Name} §7v{toBebought.Value.Version} §6bought"));
        if (toBebought.Value.Price != 0)
            await topupApi.TopUpCustomPostAsync(sellerUserId, new()
            {
                Amount = toBebought.Value.Price * 70 / 100,
                Reference = $"config sale {name} to {socket.SessionInfo.McName}",
                ProductId = "config-sell"
            });
        socket.ExecuteCommand($"/cofl loadconfig {sellerUserId} {name}");

    }
}
