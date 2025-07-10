using System;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class TaskCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var extractedState = await socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        if (extractedState.KatStatus?.KatEnd > DateTime.UtcNow)
            socket.Dialog(db => db.MsgLine($"Your kat is taking care of {extractedState.KatStatus.ItemName} for another {socket.formatProvider.FormatTime(extractedState.KatStatus.KatEnd - DateTime.UtcNow)}."));
        else
            socket.Dialog(db => db.MsgLine("Doesn't look like you have an item in kat, maybe you should give a pet to kat?"));
    }
}
