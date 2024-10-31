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
        var trade = limits.Trade;
        socket.Dialog(db =>
        {
            db.MsgLine("In the last 24 hours you have:");
            NewMethod(db, trade, "traded");
            return db;
        });
    }

    private static DialogBuilder NewMethod(SocketDialogBuilder db, List<Limit> trade, string keyword)
    {
        return db.MsgLine($"{keyword} {McColorCodes.AQUA}{trade.Sum(t => t.Amount)} in {trade.Count} messages", null, string.Join('\n', trade.Select(t => t.Message)));
    }
}