using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Switches your region", "currently supported: eu, us")]
public class SwitchRegionCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var selected = JsonConvert.DeserializeObject<string>(arguments);
        if (string.IsNullOrEmpty(selected))
        {
            socket.Dialog(db => db.MsgLine($"Current region: {McColorCodes.AQUA}{socket.AccountInfo.Region}")
                .MsgLine($"Usage: /cofl switchRegion <region>")
                .MsgLine($"Available Regions: {McColorCodes.AQUA}eu, us")
                .CoflCommand<SwitchRegionCommand>("Click to switch", socket.AccountInfo.Region == "us" ? "eu" : "us", "Click to switch your region"));
            return;
        }
        if (selected == "eu")
        {
            socket.Dialog(db => db.MsgLine($"Switching to {McColorCodes.AQUA}EU"));
            socket.AccountInfo.Region = "eu";
            await socket.sessionLifesycle.AccountInfo.Update();
            socket.ExecuteCommand("/cofl start");
        }
        else if (selected == "us")
        {
            if(await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
            {
                socket.Dialog(db => db.CoflCommand<PurchaseCommand>($"You need to be {McColorCodes.AQUA}Premium+{McColorCodes.WHITE} to use the US region. [click to upgrade]", "prem+", "Click to upgrade"));
                return;
            }
            socket.Dialog(db => db.MsgLine($"Switching to {McColorCodes.AQUA}US"));
            socket.AccountInfo.Region = "us";
            await socket.sessionLifesycle.AccountInfo.Update();
            socket.ExecuteCommand("/cofl connect ws://sky-us.coflnet.com/modsocket");
        }
        else
        {
            socket.Dialog(db => db.MsgLine($"Unknown region {McColorCodes.AQUA}{selected}"));
        }
        return;
    }
}