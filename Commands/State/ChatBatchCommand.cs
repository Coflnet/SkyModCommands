using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using System.Linq;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Coflnet.Sky.Proxy.Client.Api;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Net.Http;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Upload a batch of chat
    /// </summary>
    public class ChatBatchCommand : McCommand
    {

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var batch = JsonConvert.DeserializeObject<List<string>>(arguments);
            if (batch.Count == 0)
                return;
            if (batch[0] == "You cannot view this auction!")
                socket.SendMessage(COFLNET + "You have to use a booster cookie or be on the hub island to open auctions. \nClick to warp to hub", "/hub", "warp to hub");
            if (batch.All(l => l.Contains("§a❈ Defense")))
                return; // dismiss stat update
            var config = socket.GetService<IConfiguration>();
            var playerId = socket.SessionInfo?.McName;
            if (playerId == "Ekwav" || MinecraftSocket.IsDevMode)
                Console.WriteLine("produced chat batch " + string.Join(',',batch));
            try
            {
                socket.GetService<IStateUpdateService>().Produce(playerId, new()
                {
                    ChatBatch = batch,
                    ReceivedAt = DateTime.UtcNow,
                    PlayerId = playerId,
                    Kind = UpdateMessage.UpdateKind.CHAT,
                    UserId = socket.UserId
                });

                foreach (var item in batch)
                {
                    await ProcessLine(socket, batch, item);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("chat produce failed " + e);
            }
        }

        private async Task ProcessLine(MinecraftSocket socket, List<string> batch, string item)
        {
            if (item.StartsWith("You purchased"))
            {
                socket.GetService<PreApiService>().PurchaseMessage(socket, item);
            }
            if (item.StartsWith("BIN Auction started"))
                await socket.GetService<PreApiService>().ListingMessage(socket, item);
            var secondLine = batch.Last();
            if (secondLine.StartsWith("You claimed"))
                await UpdateSellerAuction(socket, secondLine);
            if (item.StartsWith("Bid of"))
                await CheckBid(socket, item);
            if (item.StartsWith("You must set it to at least"))
                socket.SessionInfo.ToLowListingAttempt = item;
            if (item.StartsWith("Profile ID: "))
            {
                Console.WriteLine("found profile id " + item);
                socket.SessionInfo.ProfileId = item.Substring("Profile ID: ".Length);
            }
            if (item.Contains("Chameleon"))
            {
                Console.WriteLine("Chameleon: " + item);
            }

            if (item.StartsWith("\nClick th"))
                {
                    Console.WriteLine("found reward link");
                    var match = Regex.Match(item, @"(https://rewards.hypixel.net/claim-reward/[a-f0-9-]+)");
                    if (match.Success)
                    {
                        try
                        {
                            await RewardHandler.SendRewardOptions(socket, match);
                        }
                        catch (Exception e)
                        {
                            dev.Logger.Instance.Error(e, "Failed to get reward options");
                            socket.Dialog(db => db.MsgLine("Failed to get reward options. Please report this on our discord."));
                        }
                    }
                }
        }



        private async Task CheckBid(MinecraftSocket socket, string line)
        {
            var uuid = socket.SessionInfo.VerificationBidAuctioneer;
            Activity.Current?.Log("checking bid for player " + uuid);
            if (uuid == null)
                return;
            var match = Regex.Match(line, @"Bid of (\d+) coins.*");
            var bid = int.Parse(match.Groups[1].Value);
            if (bid != socket.SessionInfo.VerificationBidAmount)
            {
                socket.Dialog(db => db.MsgLine($"You bid the wrong amount. Please bid the correct amount of {socket.SessionInfo.VerificationBidAmount} to verify your account."));
                return;
            }
            var baseApi = socket.GetService<IBaseApi>();
            await baseApi.BaseAhPlayerIdPostWithHttpInfoAsync(uuid);
            socket.Dialog(db => db.MsgLine($"Registered your verification bid. Waiting for the hypixel api to update to verify the bid.", null, "This can take up to 1 minute."));
        }

        private static async Task UpdateSellerAuction(MinecraftSocket socket, string secondLine)
        {
            var name = Regex.Match(secondLine, @"from (\[.*\] |)(.*)'s auction!").Groups[2];
            if (string.IsNullOrEmpty(name.Value))
            {
                Activity.Current?.Log("no name found in " + secondLine);
                return;
            }
            Activity.Current?.Log("claiming " + name.Value);
            var uuid = await socket.GetPlayerUuid(name.Value);
            var baseApi = socket.GetService<IBaseApi>();
            var info = await baseApi.BaseAhPlayerIdPostWithHttpInfoAsync(uuid);
            if (info.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Activity.Current?.Log($"failed to get auction info for {name.Value} {info.StatusCode}");
                return;
            }
        }
    }
}