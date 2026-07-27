using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Leaderboard.Client.Api;
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
        }
        else if(arguments.Trim('"') == "status")
        {
            await ShowStatus(socket);
            return;
        }
        else if(arguments.Length > 2 && !int.TryParse(arguments.Trim('"'), out _))
        {
            socket.Dialog(db => db.MsgLine("Usage: /cl buyspeedboard [page|disable|enable|status]"));
            return;
        }

        await base.Execute(socket, arguments);
    }


    protected override void PrintLine(MinecraftSocket socket, DialogBuilder db, LeaderboardService.LeaderboardEntry data)
    {
        db.MsgLine($"{McColorCodes.RED}{socket.FormatPrice(-data.Score)}ms §7{data.PlayerName}", $"https://sky.coflnet.com/player/{data.PlayerId}/flips", "See flips");
    }

    public static async Task DisableBuySpeedBoard(MinecraftSocket socket, string setting = "true")
    {
        try
        {
            await socket.GetService<ISettingsApi>().UpdateSettingAsync(socket.SessionInfo.McUuid, "disable-buy-speed-board", JsonConvert.SerializeObject(setting));
        }
        catch (Exception e)
        {
            socket.GetService<ILogger<LeaderboardCommand>>().LogError(e, "Failed to opt out of buyspeedboard");
        }
        if (setting != null)
        {
            // user is opting out, best-effort remove scores they already posted this/last week
            await PurgeAlreadyPostedScores(socket);
        }
    }

    /// <summary>
    /// Computes the buyspeedboard slugs that could still contain a score for the given point in time,
    /// i.e. the current week's slug and the previous week's slug (whose ~20 day retention can still be alive).
    /// </summary>
    internal static IEnumerable<string> GetBuySpeedBoardSlugsToPurge(DateTime utcNow)
    {
        var currentWeek = utcNow.RoundDown(TimeSpan.FromDays(7));
        yield return $"sky-buyspeed-{currentWeek.ToString("yyyy-MM-dd")}";
        yield return $"sky-buyspeed-{currentWeek.AddDays(-7).ToString("yyyy-MM-dd")}";
    }

    private static async Task PurgeAlreadyPostedScores(MinecraftSocket socket)
    {
        var scoresApi = socket.GetService<IScoresApi>();
        foreach (var slug in GetBuySpeedBoardSlugsToPurge(DateTime.UtcNow))
        {
            try
            {
                await scoresApi.DeleteUserScoresAsync(slug, socket.SessionInfo.McUuid, default);
            }
            catch (Exception e)
            {
                socket.GetService<ILogger<LeaderboardCommand>>().LogError(e, $"Failed to purge already posted buyspeedboard score for slug {slug}");
            }
        }
    }

    private static async Task ShowStatus(MinecraftSocket socket)
    {
        try
        {
            var settingsApi = socket.GetService<ISettingsApi>();
            var setting = await settingsApi.GetSettingAsync(socket.SessionInfo.McUuid, "disable-buy-speed-board");
            var isDisabled = !string.IsNullOrEmpty(setting);
            var statusText = isDisabled ? $"{McColorCodes.RED}Disabled" : $"{McColorCodes.GREEN}Enabled";
            var actionText = isDisabled ? "enable" : "disable";
            
            socket.Dialog(db => db
                .MsgLine($"{McColorCodes.YELLOW}Buyspeedboard Status: {statusText}")
                .MsgLine($"{McColorCodes.GRAY}You are currently {(isDisabled ? "not showing" : "showing")} on the buyspeedboard")
                .MsgLine($"{McColorCodes.GRAY}Use {McColorCodes.AQUA}/cl buyspeedboard {actionText}{McColorCodes.GRAY} to {actionText} it"));
        }
        catch (Exception e)
        {
            socket.GetService<ILogger<LeaderboardCommand>>().LogError(e, "Failed to get buyspeedboard status");
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Failed to get buyspeedboard status"));
        }
    }

    protected override string GetBoardName()
    {
        return $"sky-buyspeed";
    }
}
