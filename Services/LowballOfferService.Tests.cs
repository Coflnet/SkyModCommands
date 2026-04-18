using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Cassandra;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Controllers;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable

namespace Coflnet.Sky.ModCommands.Services;

public class LowballOfferServiceTests
{
    [Test]
    public async Task LowballOfferStateTransitionsThroughControllerEndpoints()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var service = new InMemoryLowballOfferService(configuration);
        var controller = new LowballOfferController(service, NullLogger<LowballOfferController>.Instance);

        var auctioneerId = Guid.NewGuid();
        var userId = auctioneerId.ToString();
        var filters = new Dictionary<string, string>
        {
            ["tier"] = "epic",
            ["stars"] = "5"
        };

        var createdOffer = await service.CreateOffer(
            userId,
            new SaveAuction
            {
                Tag = "HYPERION",
                ItemName = "§6Hyperion",
                AuctioneerId = auctioneerId.ToString(),
                Count = 1,
                Context = new Dictionary<string, string>
                {
                    ["lore"] = "§7Wither Impact"
                }
            },
            915_000_000,
            new Sniper.Client.Model.PriceEstimate { Median = 1_020_000_000, Volume = 2 },
            "https://sky.coflnet.com/item/HYPERION",
            filters);

        var userOffers = ExtractOkValue<List<LowballOffer>>(await controller.GetUserOffers(userId));
        userOffers.Should().ContainSingle();
        userOffers[0].OfferId.Should().Be(createdOffer.OfferId);
        userOffers[0].ItemTag.Should().Be("HYPERION");
        userOffers[0].AskingPrice.Should().Be(915_000_000);

        var itemOffers = ExtractOkValue<List<LowballOfferByItem>>(await controller.GetItemOffers("HYPERION", filter: new Dictionary<string, string>(filters)));
        itemOffers.Should().ContainSingle();
        itemOffers[0].OfferId.Should().Be(createdOffer.OfferId);

        var filteredOutOffers = ExtractOkValue<List<LowballOfferByItem>>(await controller.GetItemOffers("HYPERION", filter: new Dictionary<string, string> { ["tier"] = "legendary" }));
        filteredOutOffers.Should().BeEmpty();

        var deleteResult = await controller.DeleteOffer(userId, createdOffer.OfferId);
        deleteResult.Should().BeOfType<OkResult>();

        var secondDeleteResult = await controller.DeleteOffer(userId, createdOffer.OfferId);
        secondDeleteResult.Should().BeOfType<NotFoundResult>();

        var userOffersAfterDelete = ExtractOkValue<List<LowballOffer>>(await controller.GetUserOffers(userId));
        userOffersAfterDelete.Should().BeEmpty();

        var itemOffersAfterDelete = ExtractOkValue<List<LowballOfferByItem>>(await controller.GetItemOffers("HYPERION", filter: new Dictionary<string, string>(filters)));
        itemOffersAfterDelete.Should().BeEmpty();
    }

    private static T ExtractOkValue<T>(ActionResult<T> actionResult) where T : class
    {
        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        okResult.Value.Should().BeOfType<T>();
        return (T)okResult.Value!;
    }

    private sealed class InMemoryLowballOfferService : LowballOfferService
    {
        private readonly object sync = new();
        private readonly List<LowballOffer> userOffers = new();
        private readonly List<LowballOfferByItem> itemOffers = new();

        public InMemoryLowballOfferService(IConfiguration configuration)
            : base(
                Mock.Of<ISession>(),
                configuration,
                NullLogger<LowballOfferService>.Instance,
                null!,
                new MinecraftLoreRenderer(NullLogger<MinecraftLoreRenderer>.Instance),
                initializeTables: false)
        {
        }

        protected override Task InsertOffersAsync(LowballOffer offer, LowballOfferByItem offerByItem)
        {
            lock (sync)
            {
                userOffers.Add(Clone(offer));
                itemOffers.Add(Clone(offerByItem));
            }

            return Task.CompletedTask;
        }

        protected override Task<List<LowballOffer>> LoadOffersByUserAsync(string userId, DateTimeOffset? before = null, int limit = 20)
        {
            lock (sync)
            {
                var results = userOffers
                    .Where(o => o.UserId == userId && (!before.HasValue || o.CreatedAt < before.Value))
                    .OrderByDescending(o => o.CreatedAt)
                    .ThenByDescending(o => o.OfferId)
                    .Take(Math.Max(1, limit))
                    .Select(Clone)
                    .ToList();
                return Task.FromResult(results);
            }
        }

        protected override Task<List<LowballOfferByItem>> LoadOffersByItemAsync(string itemTag, DateTimeOffset? before = null, int limit = 20)
        {
            lock (sync)
            {
                var results = itemOffers
                    .Where(o => o.ItemTag == itemTag && (!before.HasValue || o.CreatedAt < before.Value))
                    .OrderByDescending(o => o.CreatedAt)
                    .ThenByDescending(o => o.OfferId)
                    .Take(Math.Max(1, limit))
                    .Select(Clone)
                    .ToList();
                return Task.FromResult(results);
            }
        }

        protected override Task<bool> DeleteOfferAsync(string userId, Guid offerId)
        {
            lock (sync)
            {
                var existing = userOffers.FirstOrDefault(o => o.UserId == userId && o.OfferId == offerId);
                if (existing == null)
                    return Task.FromResult(false);

                userOffers.RemoveAll(o => o.UserId == userId && o.OfferId == offerId);
                itemOffers.RemoveAll(o => o.ItemTag == existing.ItemTag && o.OfferId == offerId);
                return Task.FromResult(true);
            }
        }

        protected override Task PublishToKafka(LowballOffer offer)
        {
            return Task.CompletedTask;
        }

        private static LowballOffer Clone(LowballOffer offer)
        {
            return new LowballOffer
            {
                UserId = offer.UserId,
                CreatedAt = offer.CreatedAt,
                OfferId = offer.OfferId,
                ItemTag = offer.ItemTag,
                MinecraftAccount = offer.MinecraftAccount,
                ItemName = offer.ItemName,
                ApiAuctionJson = offer.ApiAuctionJson,
                Filters = offer.Filters,
                AskingPrice = offer.AskingPrice,
                Lore = offer.Lore,
                ItemCount = offer.ItemCount,
            };
        }

        private static LowballOfferByItem Clone(LowballOfferByItem offer)
        {
            return new LowballOfferByItem
            {
                ItemTag = offer.ItemTag,
                CreatedAt = offer.CreatedAt,
                OfferId = offer.OfferId,
                UserId = offer.UserId,
                MinecraftAccount = offer.MinecraftAccount,
                ItemName = offer.ItemName,
                ApiAuctionJson = offer.ApiAuctionJson,
                Filters = offer.Filters,
                AskingPrice = offer.AskingPrice,
                Lore = offer.Lore,
                ItemCount = offer.ItemCount,
            };
        }
    }
}