using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Offer items to or register as lowballer",
    "Simplifies lowballing by not requiring",
    "you to advertise anymore as a buyer.",
    "And allows you to compare multiple offers",
    "and be visited by the highest as a seller")]
public class LowballCommand : ItemSelectCommand<LowballCommand>
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        var service = socket.GetService<LowballSerivce>();
        var offerService = socket.GetService<Coflnet.Sky.ModCommands.Services.LowballOfferService>();
        if (args.Length == 2 && args[0] == "remove")
        {
            if (offerService == null)
            {
                socket.Dialog(db => db.MsgLine("§cLowball removal not available on this server."));
                return;
            }

            if (!Guid.TryParse(args[1], out var offerId))
            {
                socket.Dialog(db => db.MsgLine("§cInvalid offer id. Usage: /cofl lowball remove <offer-uuid>"));
                return;
            }

            var userId = socket.SessionInfo.McUuid;
            var success = await offerService.DeleteOffer(userId, offerId);
            if (success)
                socket.Dialog(db => db.MsgLine($"§aRemoved lowball offer {offerId}"));
            else
                socket.Dialog(db => db.MsgLine($"§cFailed to remove lowball offer {offerId}. It may not exist or you are not the owner."));

            return;
        }
        if (args.Length == 1)
        {
            if (args[0] == "list")
            {
                if (offerService == null)
                {
                    socket.Dialog(db => db.MsgLine("§cLowball listing not available on this server."));
                    return;
                }

                var userId = socket.SessionInfo.McUuid;
                var offers = await offerService.GetOffersByUser(userId, null, 100);
                if (offers == null || offers.Count == 0)
                {
                    socket.Dialog(db => db.MsgLine("§7You have no active lowball offers."));
                    return;
                }

                socket.Dialog(db => db.MsgLine("§7Your lowball offers:"));
                foreach (var o in offers)
                {
                    // show item name, price and created time with a clickable [rm]
                    var line = $"§6{o.ItemName} §7- {socket.FormatPrice(o.AskingPrice)} coins §8({o.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm}) ";
                    var removeCmd = $"/cofl lowball remove {o.OfferId}";
                    socket.Dialog(db => db.MsgLine(line + "§c[rm]", removeCmd, "Remove this lowball offer"));
                }
                return;
            }
            if (args[0] == "on")
            {
                service.Enable(socket);
                socket.Dialog(db => db.MsgLine("§aLowballing is now enabled, you may receive lowballs matching your filter. To permanently enable lowballing use /cofl lowball always"));
                return;
            }
            else if (args[0] == "off")
            {
                service.Disable(socket);
                socket.Dialog(db => db.MsgLine("§cLowballing is now disabled, you will no longer receive lowball offers."));
                return;
            }
            else if (args[0] == "always")
            {
                socket.sessionLifesycle.AccountSettings.Value.BlockLowballs = false;
                await socket.sessionLifesycle.AccountSettings.Update();
                service.Enable(socket);
                socket.Dialog(db => db.MsgLine("§aLowballing is now enabled permanently."));
                return;
            }
            else if (args[0] == "never")
            {
                socket.sessionLifesycle.AccountSettings.Value.BlockLowballs = true;
                await socket.sessionLifesycle.AccountSettings.Update();
                service.Disable(socket);
                socket.Dialog(db => db.MsgLine("§cLowballing is now disabled permanently."));
                return;
            }
            else if (args[0] == "offer")
            {
                socket.Dialog(db => db.MsgLine("§cPlease specify a price to offer and a slot, usage: /cofl lowball offer <price> <slot>"));
                return;
            }
            else if (args[0] == "help")
            {
                socket.Dialog(db => db.MsgLine("§7Lowball command help:\n" +
                                                "§a/cofl lowball on - Enable lowballing\n" +
                                                "§c/cofl lowball off - Disable lowballing\n" +
                                                "§a/cofl lowball always - Enable lowballing permanently\n" +
                                                "§c/cofl lowball never - Disable lowballing permanently\n" +
                                                "§6/cofl lowball status - Show current lowball status\n" +
                                                "§6/cofl lowball - Offer an item in your inventory"));
                return;
            }
            else if (args[0] == "status")
            {
                var isPermanentlyBlocked = socket.sessionLifesycle.AccountSettings.Value.BlockLowballs;
                var isCurrentlyEnabled = service.IsEnabled(socket);
                
                string statusText;
                if (isPermanentlyBlocked)
                {
                    statusText = $"{McColorCodes.RED}Permanently Disabled";
                }
                else if (isCurrentlyEnabled)
                {
                    statusText = $"{McColorCodes.GREEN}Enabled";
                }
                else
                {
                    statusText = $"{McColorCodes.YELLOW}Temporarily Disabled";
                }
                
                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.YELLOW}Lowball Status: {statusText}")
                    .MsgLine($"{McColorCodes.GRAY}Permanent setting: {(isPermanentlyBlocked ? $"{McColorCodes.RED}Never" : $"{McColorCodes.GREEN}Allow")}")
                    .MsgLine($"{McColorCodes.GRAY}Current session: {(isCurrentlyEnabled ? $"{McColorCodes.GREEN}Enabled" : $"{McColorCodes.YELLOW}Disabled")}")
                    .MsgLine($"{McColorCodes.GRAY}Use {McColorCodes.AQUA}/cofl lowball help{McColorCodes.GRAY} to see all options"));
                return;
            }
            socket.Dialog(db => db.MsgLine($"{McColorCodes.GRAY}To register for lowballing, use {McColorCodes.AQUA}/cofl lowball on", "/cofl lowball on")
                .MsgLine($"{McColorCodes.GRAY}full help is available with {McColorCodes.AQUA}/cofl lowball help", "/cofl lowball help"));
            await HandleSelectionOrDisplaySelect(socket, args, "offer", $"Offer this item to lowballers: \n");
            return;
        }
        else if (args[0] == "offer" && args.Length > 1)
        {
            var price = Coflnet.Sky.Core.NumberParser.Long(args[1]);
            await HandleSelectionOrDisplaySelect(socket, args, "offer " + price, $"Offer this item to lowballers: \n");
        }
        else
        {
            socket.Dialog(db => db.MsgLine("§cInvalid arguments for lowball command. Usage: /cofl lowball [offer|on] " + arguments));
            return;
        }
    }

    /// <summary>
    /// Only show the user inventory directly
    /// </summary>
    /// <param name="inventory"></param>
    /// <returns></returns>
    protected override List<PlayerState.Client.Model.Item> FilterItems(List<PlayerState.Client.Model.Item> inventory)
    {
        return GetActualInventory(inventory);
    }

    public static List<PlayerState.Client.Model.Item> GetActualInventory(List<PlayerState.Client.Model.Item> inventory)
    {
        return inventory.AsEnumerable().Reverse().Take(4 * 9).Reverse().ToList();
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string context, PlayerState.Client.Model.Item item)
    {
        if (!context.StartsWith("offer "))
        {

            socket.Dialog(db => db.MsgLine($"§cInvalid context for lowball command: {context}"));
            return;
        }

        if (context.Length <= "offer xy".Length)
        {
            var auction = CreateLowballAuction(item, socket.SessionInfo.McUuid);
            if (auction.FlatenedNBT.ContainsKey("donated_museum"))
            {
                socket.Dialog(db => db.MsgLine($"§cYou cannot trade museum items, please select another item."));
                return;
            }
            var price = await socket.GetService<ISniperClient>().GetPrices([auction]);
            Console.WriteLine(JsonConvert.SerializeObject(item));
            Console.WriteLine(JsonConvert.SerializeObject(auction));
            Console.WriteLine(JsonConvert.SerializeObject(price));
            var highPrice = price[0].Median * 0.91;
            var mediumPrice = price[0].Median * 0.82;
            var lowPrice = price[0].Median * 0.70;
            var serivce = socket.GetService<LowballSerivce>();
            var highBuyerCount = await GetBuyerCount(serivce, auction, highPrice, price);
            var mediumBuyerCount = await GetBuyerCount(serivce, auction, mediumPrice, price);
            var lowBuyerCount = await GetBuyerCount(serivce, auction, lowPrice, price);
            var index = context.Split(' ').Last();
            socket.Dialog(db => db.MsgLine($"§7[§6§lOffer§7] §r{item.ItemName}", null, $"{item.ItemName}\n{item.Description}")
                .CoflCommand<LowballCommand>($"At: §a{socket.FormatPrice(highPrice)} coins: {McColorCodes.YELLOW}{highBuyerCount} buyers\n", $"offer {highPrice} {index}", $"offer item for\n{socket.FormatPrice(highPrice)} ")
                .CoflCommand<LowballCommand>($"At: §e{socket.FormatPrice(mediumPrice)} coins: {McColorCodes.YELLOW}{mediumBuyerCount} buyers\n", $"offer {mediumPrice} {index}", $"offer item for\n{socket.FormatPrice(mediumPrice)} ")
                .CoflCommand<LowballCommand>($"At: §c{socket.FormatPrice(lowPrice)} coins: {McColorCodes.YELLOW}{lowBuyerCount} buyers\n", $"offer {lowPrice} {index}", $"offer item for\n{socket.FormatPrice(lowPrice)} ")
                .MsgLine($"From ah in ~{socket.FormatPrice(1 / price[0].Volume * 24)} hours: ~{socket.FormatPrice(price[0].Median * 0.95)} coins"));
            // 5% for fees and likelyness of relist fees
            return;
        }
        else
        {
            var price = Core.NumberParser.Long(context.Substring(6));
            var auction = CreateLowballAuction(item, socket.SessionInfo.McUuid);
            var priceEstimate = await socket.GetService<ISniperClient>().GetPrices([auction]);
            if (priceEstimate.Count == 0)
            {
                socket.Dialog(db => db.MsgLine($"§cNo price estimate found for {item.ItemName}"));
                return;
            }
            var serivce = socket.GetService<LowballSerivce>();
            var buyerCount = await GetBuyerCount(serivce, auction, price, priceEstimate);
            socket.Dialog(db => db.MsgLine($"§7[§6§lLowball Offer§7]§r\n{item.ItemName}")
                .MsgLine($"You offered {socket.FormatPrice(price)} coins to lowballers, {McColorCodes.YELLOW}{buyerCount} buyers are interested{McColorCodes.GRAY} in this item at this price currently and may visit your island."));
            Console.WriteLine($"received '{context}'");
            await serivce.Offer(auction, price, priceEstimate[0], socket);
            try
            {
                var fullLink  = await HotkeyCommand.GetLinkWithFilters(socket, auction);
                await socket.GetService<LowballOfferService>().CreateOffer(socket.SessionInfo.McUuid, auction, price,  priceEstimate.First(), fullLink);
            }
            catch (Exception e)
            {
                socket.Error(e, "Failed to create lowball offer in database");
            }
        }
    }

    internal static Core.SaveAuction CreateLowballAuction(PlayerState.Client.Model.Item item, string sellerUuid)
    {
        var auction = ConvertToAuction(item);
        auction.AuctioneerId = sellerUuid;
        auction.Context = new Dictionary<string, string>()
        {
            { "lore", item.Description ?? string.Empty }
        };
        return auction;
    }

    private async Task<int> GetBuyerCount(LowballSerivce serivce, Core.SaveAuction auction, double offerPrice, List<Sniper.Client.Model.PriceEstimate> price)
    {
        var originalHighestBidAmount = auction.HighestBidAmount;
        auction.HighestBidAmount = (long)offerPrice;
        try
        {
            return await serivce.MatchCount(auction, price[0]);
        }
        finally
        {
            auction.HighestBidAmount = originalHighestBidAmount;
        }
    }
}

public class LowballSerivce
{
    private const string OfferChannel = "lowball:offer";
    private const string MatchCountRequestChannel = "lowball:match-count-request";
    private const string MatchCountResponseChannel = "lowball:match-count-response";

    private readonly ConcurrentDictionary<string, LowballerInfo> lowballers = new();
    private readonly ConcurrentDictionary<string, PendingMatchCountRequest> pendingMatchCounts = new();
    private readonly ISubscriber subscriber;
    private readonly ILogger<LowballSerivce> logger;
    private readonly string instanceId = Guid.NewGuid().ToString("N");

    public LowballSerivce(IConnectionMultiplexer redis, ILogger<LowballSerivce> logger)
    {
        subscriber = redis.GetSubscriber();
        this.logger = logger;
        subscriber.Subscribe(RedisChannel.Literal(OfferChannel), (channel, message) =>
        {
            _ = HandleDistributedOffer(message);
        });
        subscriber.Subscribe(RedisChannel.Literal(MatchCountRequestChannel), (channel, message) =>
        {
            _ = HandleMatchCountRequest(message);
        });
        subscriber.Subscribe(RedisChannel.Literal(MatchCountResponseChannel), (channel, message) =>
        {
            HandleMatchCountResponse(message);
        });
    }

    public class LowballerInfo
    {
        public MinecraftSocket Socket { get; set; }
        public DateTime Registered { get; set; }
    }

    internal sealed class PendingMatchCountRequest
    {
        private readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int totalCount;
        private int responseCount;
        private int expectedResponses = -1;

        public void AddResponse(int count)
        {
            Interlocked.Add(ref totalCount, count);
            Interlocked.Increment(ref responseCount);
            TryComplete();
        }

        public void SetExpectedResponses(int expectedResponses)
        {
            Volatile.Write(ref this.expectedResponses, expectedResponses);
            TryComplete();
        }

        public async Task<int> WaitAsync(TimeSpan timeout)
        {
            if (Volatile.Read(ref expectedResponses) == 0)
                return 0;

            var completed = await Task.WhenAny(completion.Task, Task.Delay(timeout)).ConfigureAwait(false);
            return completed == completion.Task ? await completion.Task.ConfigureAwait(false) : Volatile.Read(ref totalCount);
        }

        private void TryComplete()
        {
            var expected = Volatile.Read(ref expectedResponses);
            if (expected < 0)
                return;

            if (Volatile.Read(ref responseCount) >= expected)
                completion.TrySetResult(Volatile.Read(ref totalCount));
        }
    }

    private sealed class DistributedLowballOffer
    {
        public string OriginInstanceId { get; set; }
        public LowballOffer Offer { get; set; }
    }

    private sealed class MatchCountRequest
    {
        public string RequestId { get; set; }
        public string OriginInstanceId { get; set; }
        public Core.SaveAuction Auction { get; set; }
        public Sniper.Client.Model.PriceEstimate Estimate { get; set; }
    }

    private sealed class MatchCountResponse
    {
        public string RequestId { get; set; }
        public string OriginInstanceId { get; set; }
        public int Count { get; set; }
    }

    public async Task<int> MatchCount(Core.SaveAuction auction, Sniper.Client.Model.PriceEstimate est)
    {
        var localCount = MatchCountLocal(auction, est);
        var remoteCount = await GetRemoteMatchCount(auction, est).ConfigureAwait(false);
        return localCount + remoteCount;
    }

    private int MatchCountLocal(Core.SaveAuction auction, Sniper.Client.Model.PriceEstimate est)
    {
        var count = 0;
        var keysToRemove = new List<string>();
        foreach (var item in lowballers)
        {
            var median = new Core.LowPricedAuction()
            {
                Auction = auction,
                TargetPrice = est.Median,
                DailyVolume = est.Volume,
                Finder = Core.LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            if (item.Value?.Socket == null || item.Value.Socket.IsClosed)
            {
                keysToRemove.Add(item.Key);
                continue;
            }
            if (item.Value.Socket.ModAdapter is AfVersionAdapter)
                continue; // skip auto-flippers, only manual users are lowball buyers
            var matchInfo = item.Value.Socket.Settings.MatchesSettings(FlipperService.LowPriceToFlip(median));
            if (matchInfo.Item1)
            {
                count++;
            }
        }
        foreach (var key in keysToRemove)
        {
            lowballers.Remove(key, out _);
        }
        return count;
    }

    private async Task<int> GetRemoteMatchCount(Core.SaveAuction auction, Sniper.Client.Model.PriceEstimate est)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var pendingRequest = new PendingMatchCountRequest();
        pendingMatchCounts[requestId] = pendingRequest;
        try
        {
            var request = new MatchCountRequest()
            {
                RequestId = requestId,
                OriginInstanceId = instanceId,
                Auction = auction,
                Estimate = est
            };
            var subscriberCount = await subscriber.PublishAsync(
                RedisChannel.Literal(MatchCountRequestChannel),
                JsonConvert.SerializeObject(request)).ConfigureAwait(false);
            pendingRequest.SetExpectedResponses(Math.Max(0, (int)subscriberCount - 1));
            return await pendingRequest.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to get remote lowball match count");
            return 0;
        }
        finally
        {
            pendingMatchCounts.TryRemove(requestId, out _);
        }
    }

    private async Task HandleDistributedOffer(RedisValue message)
    {
        try
        {
            var distributedOffer = JsonConvert.DeserializeObject<DistributedLowballOffer>(message!);
            if (distributedOffer?.Offer == null || distributedOffer.OriginInstanceId == instanceId)
                return;

            NotifyUsers(distributedOffer.Offer);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to handle distributed lowball offer");
        }
        await Task.CompletedTask;
    }

    private async Task HandleMatchCountRequest(RedisValue message)
    {
        try
        {
            var request = JsonConvert.DeserializeObject<MatchCountRequest>(message!);
            if (request == null || request.OriginInstanceId == instanceId || request.Auction == null || request.Estimate == null)
                return;

            var response = new MatchCountResponse()
            {
                RequestId = request.RequestId,
                OriginInstanceId = request.OriginInstanceId,
                Count = MatchCountLocal(request.Auction, request.Estimate)
            };
            await subscriber.PublishAsync(
                RedisChannel.Literal(MatchCountResponseChannel),
                JsonConvert.SerializeObject(response)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to handle lowball match count request");
        }
    }

    private void HandleMatchCountResponse(RedisValue message)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<MatchCountResponse>(message!);
            if (response == null || response.OriginInstanceId != instanceId)
                return;

            if (pendingMatchCounts.TryGetValue(response.RequestId, out var pendingRequest))
                pendingRequest.AddResponse(response.Count);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to handle lowball match count response");
        }
    }

    internal void Enable(MinecraftSocket socket)
    {
        lowballers[socket.SessionInfo.McUuid] = new LowballerInfo()
        {
            Socket = socket,
            Registered = DateTime.Now
        };
    }

    internal bool IsEnabled(MinecraftSocket socket)
    {
        return lowballers.ContainsKey(socket.SessionInfo.McUuid);
    }

    internal void Disable(MinecraftSocket value)
    {
        if (value == null)
        {
            return;
        }
        if (lowballers.ContainsKey(value.SessionInfo.McUuid))
        {
            lowballers.Remove(value.SessionInfo.McUuid, out _);
        }
        else
        {
            value.Dialog(db => db.MsgLine($"§cYou are not registered for lowballing, use {McColorCodes.AQUA}/cofl lowball on{McColorCodes.RESET} to enable it."));
        }
    }

    internal async Task Offer(Core.SaveAuction auction, long price, Sniper.Client.Model.PriceEstimate priceEstimate, MinecraftSocket socket)
    {
        auction.HighestBidAmount = price;
        var lowballOffer = new LowballOffer()
        {
            Auction = auction,
            Price = price,
            PriceEstimate = priceEstimate,
            SellerName = socket.SessionInfo.McName
        };
        NotifyUsers(lowballOffer);
        await PublishOffer(lowballOffer).ConfigureAwait(false);
    }

    private async Task PublishOffer(LowballOffer lowballOffer)
    {
        try
        {
            await subscriber.PublishAsync(
                RedisChannel.Literal(OfferChannel),
                JsonConvert.SerializeObject(new DistributedLowballOffer()
                {
                    OriginInstanceId = instanceId,
                    Offer = lowballOffer
                })).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to publish distributed lowball offer");
        }
    }

    private void NotifyUsers(LowballOffer lowballOffer)
    {
        var keysToRemove = new List<string>();
        foreach (var item in lowballers)
        {
            if (item.Value.Socket.IsClosed || item.Value.Socket.HasFlippingDisabled())
            {
                keysToRemove.Add(item.Key);
                continue;
            }
            var median = new Core.LowPricedAuction()
            {
                Auction = lowballOffer.Auction,
                TargetPrice = lowballOffer.PriceEstimate.Median,
                DailyVolume = lowballOffer.PriceEstimate.Volume,
                Finder = Core.LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            var instance = FlipperService.LowPriceToFlip(median);
            var matchInfo = item.Value.Socket.Settings.MatchesSettings(instance);
            if (matchInfo.Item1)
            {
                var sellerName = lowballOffer.SellerName;
                var flipMessage = item.Value.Socket.formatProvider.FormatFlip(instance);
                item.Value.Socket.Dialog(db => db.Msg($"§7[§6§lLowball Offer§7]§r from {McColorCodes.AQUA}{sellerName}")
                    .MsgLine(flipMessage, "/visit " + sellerName, $"Click to visit {sellerName} to complete the trade"));
            }
            else
                Console.WriteLine($"Lowball offer {lowballOffer.Auction.ItemName} for {lowballOffer.Price} coins did not match for {item.Value.Socket.SessionInfo.McName}, reason: {matchInfo.Item2}");
        }
        foreach (var key in keysToRemove)
        {
            lowballers.Remove(key, out _);
        }
    }

    public class LowballOffer
    {
        public Core.SaveAuction Auction { get; set; }
        public long Price { get; set; }
        public Sniper.Client.Model.PriceEstimate PriceEstimate { get; set; }
        public string SellerName { get; set; }
    }
}
