using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Leaderboard.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerName.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Flippers with the most profit", "Most profit in the current week", "Supports pagination with /cl lb <page>")]
public class LeaderboardCommand : McCommand
{
    public override bool IsPublic => true;
    protected virtual string Heading => $"Top players for this week:";

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var leaderbaordApi = socket.GetService<ILeaderboardService>();
        var args = Convert<string>(arguments);
        if(args == "hideme")
        {
            if (!await socket.ReguirePremPlus())
            {
                return;
            }
            await leaderbaordApi.HideAccount(socket.UserId, socket.SessionInfo.McUuid, socket.sessionLifesycle.TierManager.ExpiresAt);
            socket.Dialog(db => db.MsgLine("You have been hidden from the flipper leaderboard until your premium plus expires."));
            return;
        }
        var isPremPlus = await socket.UserAccountTier() >= Shared.AccountTier.PREMIUM_PLUS;
        var api = socket.GetService<IScoresApi>();
        string boardSlug = GetBoardName();
        int.TryParse(args, out var page);
        if (page > 0)
            page--;
        var ownTask = api.ScoresLeaderboardSlugUserUserIdRankGetAsync($"{boardSlug}-{DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd")}", socket.SessionInfo.McUuid);
        var leaderboardData = await leaderbaordApi.GetTopFlippers(boardSlug, DateTime.UtcNow, page, 10);
        var rank = await ownTask;
        socket.Dialog(db => db.If(() => isPremPlus, (db) => db.MsgLine(Heading).ForEach(leaderboardData, (db, data) =>
        {
            PrintLine(socket, db, data);
        }), db => db.MsgLine("To see the top results you need to have premium plus.")).MsgLine($"You are rank: §6{socket.FormatPrice(rank)}"));

        await RefreshAllOlderthanOneHour(socket, leaderboardData.ToList());
    }

    private static async Task RefreshAllOlderthanOneHour(MinecraftSocket socket, System.Collections.Generic.List<LeaderboardService.LeaderboardEntry> leaderboardData)
    {
        await socket.GetService<FlipTrackingService>().GetPlayerFlips(
            leaderboardData.Where(l => l.Timestamp < DateTime.UtcNow.AddHours(-1)).Select(l => l.PlayerId),
            TimeSpan.FromDays(7));
    }

    protected virtual void PrintLine(MinecraftSocket socket, DialogBuilder db, LeaderboardService.LeaderboardEntry data)
    {
        db.MsgLine($"§6{socket.FormatPrice(data.Score)} §7{data.PlayerName}", $"https://sky.coflnet.com/player/{data.PlayerId}/flips", "See flips");
    }

    protected virtual string GetBoardName()
    {
        return $"sky-flippers";
    }
}
