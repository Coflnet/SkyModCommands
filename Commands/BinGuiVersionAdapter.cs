using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core.Migrations;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.MC
{
    public class BinGuiVersionAdapter : ModVersionAdapter
    {
        public BinGuiVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;
            var isSoldService = socket.GetService<IIsSold>();
            if (isSoldService.IsSold(uuid) && !(socket.Settings?.ModSettings?.NormalSoldFlips ?? false))
            {
                if (await socket.UserAccountTier() >= AccountTier.SUPER_PREMIUM)
                {
                    socket.Error(new Exception("This auction has likely been sold to a super premium user"));
                    return true; // don't show sold flips to super premium users
                }
                var preService = socket.GetService<PreApiService>();
                var parts = await GetMessageparts(flip);
                parts.Insert(0, new ChatPart(McColorCodes.RED + "[SOLD]",
                                             "/viewauction " + uuid,
                                             $"This auction has likely been sold to a {preService.SoldToTier(uuid)} user"));
                socket.Send(Response.Create("chatMessage", parts.ToArray()));
                Activity.Current?.AddTag("sold", "true");
                socket.GetService<ILogger<BinGuiVersionAdapter>>().LogInformation($"Not sending flip {uuid} to {socket.SessionInfo.McName} because it was sold");
                return true;
            }
            long worth = GetWorth(flip);
            var shouldPlaySound = (socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false) && (flip.Profit > 1_000_000 || flip.Finder == Core.LowPricedAuction.FinderType.USER);
            socket.Send(Response.Create("flip", new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth,
                sound = new { name = shouldPlaySound ? "note.pling" : null, pitch = 1 },
                auction = new
                {
                    itemName = socket.formatProvider.GetItemName(flip.Auction),
                    enchantments = flip.Auction.Enchantments,
                    count = flip.Auction.Count,
                    startingBid = flip.Auction.StartingBid,
                    tag = flip.Auction.Tag,
                    end = flip.Auction.End,
                    start = flip.Auction.Start,
                    auctioneerId = flip.Auction.AuctioneerId,
                },
                target = flip.Target,
                finder = flip.Finder,
                render = Random.Shared.Next(3) switch
                {
                    1 => "21d837ca222cbc0bc12426f5da018c3a931b406008800960a9df112a596e7d62",
                    2 => "sea_lantern",
                    _ => "leather_leggings"
                }
            }));
            if (DateTime.UtcNow.Month == 4 && DateTime.UtcNow.Day == 1 && Random.Shared.Next(200) == 1)
            {
                await SendAprilFools();
            }

            if (flip.Profit > 2_000_000)
            {
                _ = socket.TryAsyncTimes(async () =>
                {
                    await Task.Delay(300);
                    socket.ExecuteCommand($"/cofl fresponse {uuid} {flip.Auction.StartingBid}");
                }, "fresponse", 1);
            }
            return true;
        }

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }
    }
}
