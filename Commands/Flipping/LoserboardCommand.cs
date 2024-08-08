using System;
using Coflnet.Leaderboard.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Flippers with the highest loss", "The flippers who lost the most coins in the last week", "Supports pagination with /cl lb <page>")]
public class LoserboardCommand : LeaderboardCommand
{
    protected override string Heading => $"Players with the biggest loss this week:";
    protected override string GetBoardName()
    {
        return $"sky-flippers-loosers-{DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd")}";
    }

    protected override void PrintLine(MinecraftSocket socket, DialogBuilder db, BoardScore data, string displayName)
    {
        db.MsgLine($"{McColorCodes.RED}-{socket.FormatPrice(data.Score)} §7{(displayName)}", $"https://sky.coflnet.com/player/{data.UserId}/flips", "See flips");
    }
}
