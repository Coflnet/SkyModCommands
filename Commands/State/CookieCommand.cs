using System;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class CookieCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var client = socket.GetService<IPlayerStateApi>();
        dynamic data = await client.PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        DateTime expiresAt = data.boosterCookieExpires;
        if (socket.SessionInfo.Purse > 0)
            socket.Dialog(db => db.MsgLine($"Your purse is {socket.formatProvider.FormatPrice(socket.SessionInfo.Purse)} coins"));
        if (expiresAt < DateTime.UtcNow)
        {
            socket.Dialog(db => db.MsgLine("You don't have an active cookie buff"));
            return;
        }
        socket.Dialog(db => db.MsgLine($"Your cookie expires in {socket.formatProvider.FormatTime(expiresAt - DateTime.Now)}"));
    }
}