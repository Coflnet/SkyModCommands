using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : ModVersionAdapter
    {
        private Random rng = new Random();

        public ThirdVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;
            var preService = socket.GetService<PreApiService>();
            if (preService.IsSold(uuid))
            {
                var parts = await GetMessageparts(flip);
                parts.Insert(0, new ChatPart(McColorCodes.RED + "[SOLD]",
                                             "/viewauction " + uuid,
                                             $"This auction has likely been sold to a {preService.SoldToTier(uuid)} user"));
                socket.Send(Response.Create("chatMessage", parts.ToArray()));
                Activity.Current?.AddTag("sold", "true");
                socket.GetService<ILogger<ThirdVersionAdapter>>().LogInformation($"Not sending flip {uuid} to {socket.SessionInfo.McName} because it was sold");
                return true;
            }
            long worth = GetWorth(flip);
            var flipBody = new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth = worth,
                cost = flip.Auction.StartingBid,
                sound = (string)"note.pling"
            };
            socket.Send(Response.Create("flip", flipBody));
            if (flip.Profit > 2_000_000)
            {
                socket.ExecuteCommand($"/cofl fresponse {uuid} {worth}");
                socket.ReceivedConfirm.TryAdd(uuid, flip);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    if (socket.ReceivedConfirm.TryRemove(uuid, out var value))
                    {
                        socket.Log($"Flip with id {uuid} was not confirmed\n" + JsonConvert.SerializeObject(value), LogLevel.Error);
                        Console.WriteLine($"Flip with id {uuid} was not confirmed by {socket.SessionInfo.McName} on {System.Net.Dns.GetHostName()}\n"
                                + JsonConvert.SerializeObject(value) + "\n" + JsonConvert.SerializeObject(flipBody));
                        socket.Send(Response.Create("log", $"Flip withh id {uuid} was not confirmed"));
                    }
                });
            }
            socket.Send(Response.Create("log", $"Flip withh id {uuid} was sent"));
            if (DateTime.UtcNow.Month == 4 && DateTime.UtcNow.Day == 1 && rng.Next(200) == 1)
            {
                await SendAprilFools();
            }

            if (socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false && flip.Profit > 1_000_000)
                SendSound("note.pling", (float)(1 / (Math.Sqrt((float)flip.Profit / 1_000_000) + 1)));
            return true;
        }


        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }


    }

    public class InventoryVersionAdapter : ThirdVersionAdapter
    {
        public InventoryVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
    }
}