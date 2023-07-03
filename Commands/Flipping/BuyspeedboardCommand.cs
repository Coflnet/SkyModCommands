using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Fastest buying players", "Ranked by milliseconds after grace period", "resets weekly", "you can opt out of showing up", "with §b/cl buyspeedboard disable")]
public class BuyspeedboardCommand : LeaderboardCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.SessionInfo.VerifiedMc)
            throw new CoflnetException("forbidden", "You need to be verified to use this command");
        if (arguments.Trim('"') == "disable")
        {
            await DisableBuySpeedBoard(socket);
            socket.Dialog(db => db.MsgLine("Disabled showing on buyspeedboard, you can enable it again with §b/cl buyspeedboard enable"));
            return;
        }
        if(arguments.Trim('"') == "enable")
        {
            await DisableBuySpeedBoard(socket, null);
            socket.Dialog(db => db.MsgLine("Enabled showing on buyspeedboard"));
            return;
        }
        await base.Execute(socket, arguments);
    }

    public static async Task DisableBuySpeedBoard(MinecraftSocket socket, string setting = "true")
    {
        try
        {
            await socket.GetService<ISettingsApi>().SettingsUserIdSettingKeyPostAsync(socket.SessionInfo.McUuid, "disable-buy-speed-board", setting);
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
