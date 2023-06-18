using System;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Recorded item movements (WIP)", 
    "Shows you item movements the mod detected",
    "Targets recoginizing more kinds of flips",
    "Eg. lowballing through trade menu")]
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
                var start = $" {McColorCodes.RED}-{McColorCodes.RESET}";
                if (data.Type.Value.HasFlag(PlayerState.Client.Model.TransactionType.NUMBER_1))
                    start = $" {McColorCodes.GREEN}+{McColorCodes.RESET}";

                var thing = "an item";
                if (data.ItemId == 1000001)
                    thing = $"{socket.FormatPrice(data.Amount / 10)} coins";
                // format for TimeSpan HH:mm:ss
                var format = @"hh\:mm\:ss";
                db.MsgLine($"{start} {thing} {McColorCodes.GRAY}{(DateTime.UtcNow - data.TimeStamp).ToString(format)} ago");
            }));
    }
}