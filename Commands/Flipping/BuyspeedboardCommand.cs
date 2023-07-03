using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.MC;

public class BuyspeedboardCommand : LeaderboardCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.SessionInfo.VerifiedMc)
            throw new CoflnetException("forbidden", "You need to be verified to use this command");
        if (arguments.Trim('"') == "disable")
        {
            await OptOutOfBuyspeed(socket);
            socket.Dialog(db => db.MsgLine("Disabled buyspeedboard"));
            return;
        }
        await base.Execute(socket, arguments);
    }

    public static async Task OptOutOfBuyspeed(MinecraftSocket socket)
    {
        try
        {
            await socket.GetService<ISettingsApi>().SettingsUserIdSettingKeyPostAsync(socket.SessionInfo.McUuid, "disable-buy-speed-board", "true");
        }
        catch (Exception e)
        {
            socket.GetService<ILogger<LeaderboardCommand>>().LogError(e, "Failed to opt out of buyspeedboard");
        }
    }

    protected override string GetBoardName()
    {
        return $"sky-buyspeed-{DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd")}";
    }
}
