using System;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Flippers with the highest loss", "The flippers who lost the most coins in the last week", "Supports pagination with /cl lb <page>")]
public class LoserboardCommand : LeaderboardCommand
{
    protected override string GetBoardName()
    {
        return $"sky-flippers-loosers-{DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd")}";
    }
}
