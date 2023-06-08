using System;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class TradesCommand : McCommand
{
    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var transApi = socket.GetService<ITransactionApi>();
        var transactions = await transApi.TransactionPlayerPlayerUuidGetAsync(Guid.Parse(socket.SessionInfo.McUuid), (int)TimeSpan.FromDays(2).TotalSeconds, DateTime.UtcNow);
        socket.Dialog(db => db.MsgLine($"Stored trades:")
            .ForEach(transactions, (db, data) =>
            {
                var start = "Sent";
                if (data.Type.Value.HasFlag(PlayerState.Client.Model.TransactionType.NUMBER_1))
                    start = "Received";

                var thing = "an item";
                if (data.ItemId != 1000001)
                    thing = $"{socket.FormatPrice(data.Amount / 10)} coins";

                db.MsgLine($"{start} {thing}  {(DateTime.UtcNow - data.TimeStamp).ToString("HH:mm:ss")} ago");
            }));
    }
}