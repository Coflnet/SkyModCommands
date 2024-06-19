using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class FlipSentOnProxy : McCommand
{
    public class Data
    {
        public string AuctionId { get; set; }
        public string PlayerId { get; set; }
        public DateTime Time { get; set; }
        public Guid ItemId { get; set; }
        public long Value { get; set; }
    }
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var data = JsonConvert.DeserializeObject<Data>(arguments);
        if (data.PlayerId != socket.SessionInfo.McUuid)
            throw new Exception("PlayerId does not match the session");
        await socket.GetService<IFlipReceiveTracker>().ReceiveFlip(data.AuctionId, data.PlayerId, data.Time);
        if (data.Value != 0)
            await socket.GetService<IPriceStorageService>().SetPrice(Guid.Parse(data.PlayerId), data.ItemId, data.Value);
    }
}