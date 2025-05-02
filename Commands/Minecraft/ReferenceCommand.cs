using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Sniper.Client.Api;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class ReferenceCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var uuid = Convert<string>(arguments);
            if (uuid.EndsWith("refresh"))
            {
                var sniperApi = socket.GetService<ISniperApi>();
                var parts = uuid.Split(' ');
                var tag = parts[0];
                var actualUuid = parts[1];
                Console.WriteLine($"Reassign request from {socket.UserId} for {tag} {actualUuid}");
                var result = await sniperApi.ApiSniperReassignPostAsync(tag, actualUuid);
                if (result.Any())
                {
                    socket.SendMessage("Moved the reference to another bucket, thanks for notifying");
                }
                else
                {
                    socket.SendMessage("Reference was not moved, maybe it was already correct. If not report it");
                }
                return;
            }
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
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.STONKS) ?? false)
            {
                await SniperReference(socket, uuid, flip, "STONKS");
                return;
            }
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.USER) ?? false)
            {
                socket.Dialog(d => d.MsgLine("This is a custom flip that showed up because it matched your whitelist, no references are available"));
                return;
            }
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.TFM) ?? false)
            {
                socket.Dialog(d => d.MsgLine("TFM flips have no references"));
                return;
            }
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.CraftCost) ?? false)
            {
                CraftCostFinderReferences(socket, flip);
                return;
            }
            if (flip?.Finder.HasFlag(LowPricedAuction.FinderType.AI) ?? false)
            {
                if (!flip.AdditionalProps.TryGetValue("breakdown", out var breakdown))
                {
                    socket.Dialog(d => d.MsgLine("This flip was found by the AI, but no breakdown was available :("));
                    return;
                }
                // split on first occurance of : and then add the color codes
                var lines = breakdown.Split('\n').Select(l => l.Split(':')).Select(l => $" {l[0]}: {McColorCodes.AQUA}{string.Join(":", l.Skip(1))}{McColorCodes.GRAY}");
                socket.Dialog(d => d.MsgLine($"Here is the breakdown of values:")
                    .ForEach(lines, (d, l) => d.MsgLine(l)));
                return;
            }
            socket.ModAdapter.SendMessage(new ChatPart("Caclulating references", "https://sky.coflnet.com/auction/" + uuid, "please give it a second"));
            var based = await CoreServer.ExecuteCommandWithCache<string, IEnumerable<BasedOnCommandResponse>>("flipBased", uuid);
            if (based == null)
                socket.ModAdapter.SendMessage(new ChatPart("Woops, sorry but there could be no references found or another error occured :("));
            else if (based.Count() == 0)
            {
                socket.SendMessage("No references found for that flip on your connection anymore, sorry :/");
            }
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

            await Task.Delay(200).ConfigureAwait(false);
            socket.ModAdapter.SendMessage(new ChatPart(MinecraftSocket.COFLNET + "click this to open the auction on the website (in case you want to report an error or share it)", "https://sky.coflnet.com/auction/" + uuid, "please give it a second"));
        }

        private static void CraftCostFinderReferences(MinecraftSocket socket, LowPricedAuction flip)
        {
            var breakdown = JsonConvert.DeserializeObject<Dictionary<string, float>>(flip.AdditionalProps["breakdown"]);
            socket.Dialog(d => d.MsgLine("Estimate based on craft cost sum")
                .MsgLine($"Clean item cost: {McColorCodes.AQUA}{flip.AdditionalProps["cleanCost"]}{McColorCodes.GRAY}")
                .ForEach(breakdown, (d, kv) => d.MsgLine($"{kv.Key}: {McColorCodes.AQUA}{socket.FormatPrice(kv.Value)}{McColorCodes.GRAY}")));
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
            flip.AdditionalProps.TryGetValue("reference", out var referenceId);
            Console.WriteLine(referenceId);
            Console.WriteLine(JSON.Stringify(flip.AdditionalProps));
            var references = new List<SaveAuction>();
            if (flip.AdditionalProps.ContainsKey("med"))
                foreach (var item in flip.AdditionalProps["med"].Split(','))
                {
                    try
                    {

                        if (!string.IsNullOrEmpty(item))
                            references.Add(await AuctionService.Instance.GetAuctionAsync(AuctionService.Instance.GetUuid(long.Parse(item))));
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
            if (flip.AdditionalProps.TryGetValue("closest", out var closestKey))
                parts.Add(new ChatPart($"Used key {closestKey}"));
            parts.AddRange(references.SelectMany(r =>
            {
                return RefreshReference(socket, r);


            }
            ));
            if (flip.Finder == LowPricedAuction.FinderType.SNIPER && !string.IsNullOrEmpty(referenceId))
            {
                reference = await AuctionService.Instance.GetAuctionAsync(referenceId);
                parts.Add(new ChatPart($"\n{McColorCodes.WHITE}Lowest bin auction for {reference.ItemName}:", $"/viewauction {reference.Uuid}", "open ah"));
                parts.Add(FormatAuction(socket, reference));
                parts.Add(new ChatPart($" [website]", $"https://sky.coflnet.com/auction/{reference.Uuid}", "open it on website"));
            }
            socket.ModAdapter.SendMessage(parts.ToArray());

            static IEnumerable<ChatPart> RefreshReference(MinecraftSocket socket, SaveAuction r)
            {
                yield return FormatAuction(socket, r);
                yield return new ChatPart($"{McColorCodes.GREEN}[Refresh]", $"/cofl reference {r.Tag} {r.Uuid} refresh", "This reference is wrong and should be refreshed");
            }

            static ChatPart FormatAuction(MinecraftSocket socket, SaveAuction r)
            {
                var formattedPrice = socket.FormatPrice(r.HighestBidAmount == 0 ? r.StartingBid : r.HighestBidAmount);
                return new ChatPart($"\n->{socket.formatProvider.GetRarityColor(r.Tier)} {r.ItemName}{McColorCodes.GRAY} for {McColorCodes.AQUA}{formattedPrice}{McColorCodes.GRAY} {r.End}",
                                                        "https://sky.coflnet.com/auction/" + r.Uuid,
                                                        "Click to open this auction");
            }
        }
    }
}