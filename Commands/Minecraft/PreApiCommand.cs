using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;
public class PreApiCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
        var preapiService = socket.GetService<PreApiService>();
        if (args[0] == "notify")
        {
            preapiService.AddNotify(preapiService.PreApiUserCount - 1, socket);
            socket.SendMessage($"{COFLNET}{McColorCodes.GREEN}You will be notified when there are less than {preapiService.PreApiUserCount} users using pre-api");
            return;
        }
        if (await socket.UserAccountTier() >= Shared.AccountTier.SUPER_PREMIUM)
        {
            socket.Dialog(db => db.MsgLine($"Your pre-api expires in {McColorCodes.AQUA}{socket.AccountInfo.ExpiresAt.Subtract(DateTime.UtcNow).ToString(@"mm\:ss")}", null, "Thats minutes:seconds"));
            return;
        }
        socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
            $"{McColorCodes.GOLD}You currently don't have {McColorCodes.RED}pre-api\n"
            + $"{McColorCodes.YELLOW}You can click this to purchase it\n",
            "pre_api", $"Click to purchase pre-api")
            .CoflCommand<PreApiCommand>($"notify me when there are less than {preapiService.PreApiUserCount} users using it", "notify", "Click to get notified"));
    }
}
