using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC;

public class LimitsCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var client = socket.GetService<IPlayerStateApi>();
        var limits = await client.PlayerStatePlayerIdLimitsGetAsync(socket.SessionInfo.McName);
        socket.Dialog(db =>
        {
            db.MsgLine("In the last 24 hours you have:");
            NewMethod(db, limits.TradeSent / 10, "traded");
            NewMethod(db, limits.NpcSold / 10, "sold to npc");
            return db;
        });
    }

    private static DialogBuilder NewMethod(SocketDialogBuilder db, long amount, string keyword)
    {
        return db.MsgLine($"{keyword} {amount} coins", null);
    }
}