using System;
using System.Threading.Tasks;
using Coflnet.Leaderboard.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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
        else if(arguments.Trim('"') == "enable")
        {
            await DisableBuySpeedBoard(socket, null);
            socket.Dialog(db => db.MsgLine("Enabled showing on buyspeedboard"));
            return;
        } else if(arguments.Length > 2 && !int.TryParse(arguments.Trim('"'), out _))
        {
            socket.Dialog(db => db.MsgLine("Usage: /cl buyspeedboard [page|disable|enable]"));
            return;
        }

        await base.Execute(socket, arguments);
    }


    protected override void PrintLine(MinecraftSocket socket, DialogBuilder db, BoardScore data, string displayName)
    {
        db.MsgLine($"{McColorCodes.RED}{socket.FormatPrice(-data.Score)}ms §7{(displayName)}", $"https://sky.coflnet.com/player/{data.UserId}/flips", "See flips");
    }

    public static async Task DisableBuySpeedBoard(MinecraftSocket socket, string setting = "true")
    {
        try
        {
            await socket.GetService<ISettingsApi>().SettingsUpdateSettingAsync(socket.SessionInfo.McUuid, "disable-buy-speed-board", JsonConvert.SerializeObject(setting));
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
