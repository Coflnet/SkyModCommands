using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class UploadScoreboardCommand : McCommand
{
    /*
    "SKYBLOCK CO-OP",
"www.hypixel.net",
"             ",
" (56/4.8k) Combat XP",
"Revenant Horror IV",
"Slayer Quest",
"         ",
"Bits: 10,920",
"᠅ Mithril: 4,614",
"      ",
" ⏣ Upper Mines",
" 8:40am ☀",
" Autumn 13th",
"  ",
"07/12/24 m182R"
*/
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = JsonConvert.DeserializeObject<string[]>(arguments);
        var isIronman = false;
        var isDungeon = false;
        var isBingo = false;
        var isStranded = false;
        var isRift = false;
        var isDarkAuction = false;
        var isInSkyblock = args.FirstOrDefault()?.Contains("SKYBLOCK") ?? false;
        foreach (var item in args)
        {
            if (item.Contains("SKYBLOCK GUEST"))
                return; // can't determine mode when visiting
            if (item.Contains("♲"))
                isIronman = true;
            if (item.Contains("Ⓑ"))
                isBingo = true;
            if (item.Truncate(4).Contains("☀"))
                isStranded = true;
            if (item.Contains("the catacombs"))
                isDungeon = true;
            if (item.Contains("Dark Auction"))
                isDarkAuction = true;
            if (item.Contains("Motes:") || item.Contains("The Rift"))
            {
                isRift = true;
            }
            if (item.StartsWith("Purse:") || item.StartsWith("Piggy: "))
            {
                await new UpdatePurseCommand().Execute(socket, item.Substring(7).Replace(",", "").Split(" ")[0]);
                isInSkyblock = true;
            }
        }
        var wasNotFlippable = socket.SessionInfo.IsNotFlipable;
        socket.SessionInfo.IsIronman = isIronman;
        socket.SessionInfo.IsBingo = isBingo;
        socket.SessionInfo.IsStranded = isStranded;
        socket.SessionInfo.IsDungeon = isDungeon;
        socket.SessionInfo.IsRift = isRift;
        socket.SessionInfo.IsDarkAuction = isDarkAuction;
        if (!isInSkyblock)
            socket.SessionInfo.Purse = -1;

        if (socket.CurrentRegion != "eu")
            return; // only send messages in eu
        if (wasNotFlippable && !socket.SessionInfo.IsNotFlipable && !socket.HasFlippingDisabled())
        {
            socket.Dialog(db => db.MsgLine("Flips reenabled because you left non-flippable gamemode"));
        }
        else if (!wasNotFlippable && socket.SessionInfo.IsNotFlipable && socket.SessionInfo.ConnectedAt.AddMinutes(1) < DateTime.UtcNow)
        {
            socket.Dialog(db => db.MsgLine("Flips disabled because you are in a gamemode with no auction house", null, $"You can disable flips generally with {McColorCodes.AQUA}/cofl flip never"));
        }
        var playerId = socket.SessionInfo?.McName;
        try
        {
            if(socket.sessionLifesycle?.UserId?.Value == null)
                return;
            socket.GetService<IStateUpdateService>().Produce(playerId, new()
            {
                ReceivedAt = DateTime.UtcNow,
                PlayerId = playerId,
                Kind = UpdateMessage.UpdateKind.Scoreboard,
                UserId = socket.UserId,
                Scoreboard = args
            });
        }
        catch (Exception e)
        {
            socket.GetService<ILogger<UploadScoreboardCommand>>().LogError(e, "chat produce failed");
        }
        await Task.Delay(100); // soft ratelimit
    }
}