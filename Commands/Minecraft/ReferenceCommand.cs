using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class ReferenceCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var uuid = arguments.Trim('"');
            var flip = socket.GetFlip(uuid);
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.SNIPER) ?? false)
            {
                await SniperReference(socket, uuid, flip, "sniping");
                return;
            }
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.SNIPER_MEDIAN) ?? false)
            {
                await SniperReference(socket, uuid, flip, "median sniper");
                return;
            }
            socket.ModAdapter.SendMessage(new ChatPart("Caclulating references", "https://sky.coflnet.com/auction/" + uuid, "please give it a second"));
            var based = await CoreServer.ExecuteCommandWithCache<string, IEnumerable<BasedOnCommandResponse>>("flipBased", uuid);
            if (based == null)
                socket.ModAdapter.SendMessage(new ChatPart("Woops, sorry but there could be no references found or another error occured :("));
            else
            {
                socket.ModAdapter.SendMessage(
                    based.OrderBy(b => b.highestBid).Skip(based.Count() / 2).Take(3)
                    .Select(ToMessage(socket))
                    .Append(new ChatPart("\nThe 3 most recent references are"))
                    .Concat(based.OrderByDescending(b => b.end).Take(3)
                    .Select(ToMessage(socket)))
                    .Append(new ChatPart("\nThe 3 most expensive references are"))
                    .Concat(based.OrderByDescending(b => b.highestBid).Take(3)
                    .Select(ToMessage(socket)))
                    .ToArray());
            }

            await Task.Delay(200);
            socket.ModAdapter.SendMessage(new ChatPart(MinecraftSocket.COFLNET + "click this to open the auction on the website (in case you want to report an error or share it)", "https://sky.coflnet.com/auction/" + uuid, "please give it a second"));
        }

        private static Func<BasedOnCommandResponse, ChatPart> ToMessage(MinecraftSocket socket)
        {
            return b => new ChatPart(
                                        $"\n-> {b.ItemName} for {McColorCodes.AQUA}{socket.FormatPrice(b.highestBid)}{McColorCodes.GRAY} {b.end}",
                                        "https://sky.coflnet.com/auction/" + b.uuid,
                                        "Click to open this auction");
        }

        private async Task SniperReference(MinecraftSocket socket, string uuid, LowPricedAuction flip, string algo)
        {
            var referenceId = flip.AdditionalProps["reference"].Trim('"');
            if (referenceId == null)
            {
                socket.Log("reference is missing", Microsoft.Extensions.Logging.LogLevel.Error);
                socket.ModAdapter.SendMessage(new ChatPart(COFLNET + "The reference for this flip could not be retrieved. It got lost"));
                return;
            }
            Console.WriteLine(referenceId);
            Console.WriteLine(JSON.Stringify(flip.AdditionalProps));
            var references = new List<SaveAuction>();
            if (flip.AdditionalProps.ContainsKey("med"))
                foreach (var item in flip.AdditionalProps["med"].Split(','))
                {
                    try
                    {

                        if (!string.IsNullOrEmpty(item))
                            references.Add(await AuctionService.Instance.GetAuctionAsync(item));
                    }
                    catch (Exception e)
                    {
                        socket.Log(e.ToString());
                    }
                }
            SaveAuction reference = null;
            Console.WriteLine(JSON.Stringify(reference));
            var explanation = "This flip finder keeps reference auctions in RAM which makes it faster\nclick this to open the flip on website";
            var parts = new List<ChatPart>();
            parts.Add(new ChatPart($"{COFLNET}Finder algorithm: {algo}\n", "https://sky.coflnet.com/auction/" + uuid, explanation));
            //   parts.Add(new ChatPart($"It was compared to {McColorCodes.AQUA} these auctions {DEFAULT_COLOR}, open ah", $"/viewauction {reference.Uuid}", McColorCodes.GREEN + "open it on ah"));
            parts.AddRange(references.Select(r =>
                new ChatPart($"\n->{socket.formatProvider.GetRarityColor(r.Tier)} {r.ItemName}{McColorCodes.GRAY} for {McColorCodes.AQUA}{socket.FormatPrice(r.HighestBidAmount)}{McColorCodes.GRAY} {r.End}",
                                        "https://sky.coflnet.com/auction/" + r.Uuid,
                                        "Click to open this auction")
            ));
            if (flip.Finder == LowPricedAuction.FinderType.SNIPER && !string.IsNullOrEmpty(referenceId))
            {
                reference = await AuctionService.Instance.GetAuctionAsync(referenceId);
                parts.Add(new ChatPart($"\n{McColorCodes.WHITE}AH LBIN: {reference.ItemName}", $"/viewauction {reference.Uuid}", "open ah"));
                parts.Add(new ChatPart($" [website]", $"https://sky.coflnet.com/auction/{reference.Uuid}", "open it on website"));
            }
            socket.ModAdapter.SendMessage(parts.ToArray());
        }
    }
}