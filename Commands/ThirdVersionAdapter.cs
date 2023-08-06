using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Newtonsoft.Json;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : ModVersionAdapter
    {
        private Random rng = new Random();

        public ThirdVersionAdapter(MinecraftSocket socket) : base(socket)
        {
            socket.TryAsyncTimes(async () =>
            {
                await Task.Delay(5000);
                SendOutDated();
            }, "updatemsg");
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;
            var preService = socket.GetService<PreApiService>();
            if (preService.IsSold(uuid) && !(socket.Settings?.ModSettings?.NormalSoldFlips ?? false))
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
                (socket as MinecraftSocket).ReceivedConfirm.TryAdd(uuid, flip);
                _ = socket.TryAsyncTimes(async () =>
                {
                    if ((socket.AccountInfo?.Tier ?? 0) >= Shared.AccountTier.SUPER_PREMIUM)
                    {
                        // make sure receival is published
                        socket.GetService<PreApiService>().PublishReceive(uuid);
                    }
                    await Task.Delay(1000);
                    if ((socket as MinecraftSocket).ReceivedConfirm.TryRemove(uuid, out var value))
                    {
                        socket.Log($"Flip with id {uuid} was not confirmed\n" + JsonConvert.SerializeObject(value), LogLevel.Error);
                        Console.WriteLine($"Flip with id {uuid} was not confirmed by {socket.SessionInfo.McName} on {System.Net.Dns.GetHostName()}\n"
                                + JsonConvert.SerializeObject(value) + "\n" + JsonConvert.SerializeObject(flipBody));
                        socket.Send(Response.Create("log", $"Flip withh id {uuid} was not confirmed"));
                    }
                }, "flipConfirm" + uuid, 1);
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

        private void SendOutDated()
        {
            SendMessage(new DialogBuilder().MsgLine("There is a newer mod version available. Please update as soon as possible. \nYou can click this to be redirected to the download.",
                                        "https://github.com/Coflnet/skyblockmod/releases",
                                        "opens github"));
        }
    }

    public class InventoryVersionAdapter : ThirdVersionAdapter
    {
        public InventoryVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
    }
}