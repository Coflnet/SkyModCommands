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

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
            throw new CoflnetException("forbidden", "You need to have at least premium plus to use this command");
        var api = socket.GetService<IScoresApi>();
        var nameApi = socket.GetService<IPlayerNameApi>();
        string boardSlug = GetBoardName();
        int.TryParse(arguments.Trim('"'), out var page);
        var ownTask = api.ScoresLeaderboardSlugUserUserIdRankGetAsync(boardSlug, socket.SessionInfo.McUuid);
        var leaderboardData = await api.ScoresLeaderboardSlugGetAsync(boardSlug, page * 10, 10);
        var names = await nameApi.PlayerNameNamesBatchPostAsync(leaderboardData.Select(d => d.UserId).ToList());
        var rank = await ownTask;
        socket.Dialog(db => db.MsgLine($"Top players for this week:").ForEach(leaderboardData, (db, data) =>
        {
            var displayName = names.Where(n => n.Key == data.UserId).Select(d => d.Value).FirstOrDefault() ?? "unknown";
            PrintLine(socket, db, data, displayName);
        }).MsgLine($"You are rank: §6{socket.FormatPrice(rank)}"));
    }

    protected virtual void PrintLine(MinecraftSocket socket, DialogBuilder db, BoardScore data, string displayName)
    {
        db.MsgLine($"§6{socket.FormatPrice(data.Score)} §7{(displayName)}", $"https://sky.coflnet.com/player/{data.UserId}/flips", "See flips");
    }

    protected virtual string GetBoardName()
    {
        return $"sky-flippers-{DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd")}";
    }
}
