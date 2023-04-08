using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;
public class UploadScoreboardCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = JsonConvert.DeserializeObject<string[]>(arguments);
        var isIronman = false;
        var isDungeon = false;
        var isBingo = false;
        var isStranded = false;
        foreach (var item in args)
        {

            if (item.Contains("♲"))
                isIronman = true;
            if (item.Contains("Ⓑ"))
                isBingo = true;
            if (item.Truncate(4).Contains("☀"))
                isStranded = true;
            if (item.Contains("the catacombs"))
                isDungeon = true;
            if(item.StartsWith("Purse:"))
                await new UpdatePurseCommand().Execute(socket,item.Substring(7).Replace(",","").Split(" ")[0]);
        }
        var wasNotFlippable = socket.SessionInfo.IsNotFlipable;
        socket.SessionInfo.IsIronman = isIronman;
        socket.SessionInfo.IsBingo = isBingo;
        socket.SessionInfo.IsStranded = isStranded;
        socket.SessionInfo.IsDungeon = isDungeon;
        if (wasNotFlippable && !socket.SessionInfo.IsNotFlipable)
        {
            socket.Dialog(db => db.MsgLine("Flips reenabled because you left non-flippable gamemode"));
        }
        else if (!wasNotFlippable && socket.SessionInfo.IsNotFlipable)
        {
            socket.Dialog(db => db.MsgLine("Flips disabled because you are in a gamemode with no auction house"));
        }
    }
}