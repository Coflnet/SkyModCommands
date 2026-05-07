using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services.Donut;

public interface IDonutFlipSubscriptionService
{
    Task RefreshSubscriptionAsync(IFlipConnection connection);
    void RemoveConnection(IFlipConnection connection);
    Task DeliverAsync(LowPricedAuction flip);
}

public sealed class DonutFlipSubscriptionService : IDonutFlipSubscriptionService
{
    private const string DefaultPremiumProductSlug = "donut-premium";

    private readonly ConcurrentDictionary<long, IFlipConnection> subscribers = new();
    private readonly IUserApi userApi;
    private readonly ILogger<DonutFlipSubscriptionService> logger;
    private readonly string premiumProductSlug;

    public DonutFlipSubscriptionService(IUserApi userApi, IConfiguration config, ILogger<DonutFlipSubscriptionService> logger)
    {
        this.userApi = userApi;
        this.logger = logger;
        premiumProductSlug = config["DONUT_PREMIUM_PRODUCT_SLUG"] ?? DefaultPremiumProductSlug;
    }

    public async Task RefreshSubscriptionAsync(IFlipConnection connection)
    {
        if (!DonutServerContext.IsDonut(connection.GameServer) || string.IsNullOrWhiteSpace(connection.UserId))
        {
            RemoveConnection(connection);
            return;
        }

        try
        {
            var ownedUntil = await userApi.UserUserIdOwnsProductSlugUntilGetAsync(connection.UserId, premiumProductSlug).ConfigureAwait(false);
            if (ownedUntil <= DateTime.UtcNow)
            {
                RemoveConnection(connection);
                return;
            }

            subscribers.AddOrUpdate(connection.Id, connection, (_, _) => connection);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to refresh Donut subscription for user {UserId}", connection.UserId);
            RemoveConnection(connection);
        }
    }

    public void RemoveConnection(IFlipConnection connection)
    {
        subscribers.TryRemove(connection.Id, out _);
    }

    public async Task DeliverAsync(LowPricedAuction flip)
    {
        if (!DonutServerContext.IsDonut(flip))
            return;

        foreach (var entry in subscribers.ToArray())
        {
            try
            {
                if (!await entry.Value.SendFlip(flip).ConfigureAwait(false))
                    subscribers.TryRemove(entry.Key, out _);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to send Donut flip to connection {ConnectionId}", entry.Key);
                subscribers.TryRemove(entry.Key, out _);
            }
        }
    }
}