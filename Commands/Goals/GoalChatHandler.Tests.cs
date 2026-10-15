using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

[NonParallelizable]
public class GoalChatHandlerTests
{
    private sealed class Handler : GoalChatHandler
    {
        public GoalList Stored;
        public int Saves;
        public int NameLoads;
        public bool Throw;
        public Action AfterLoad;
        public string SavedUserId;

        protected override Task<GoalList> LoadGoals(IMinecraftSocket socket, string userId)
        {
            if (Throw) throw new InvalidOperationException("settings down");
            AfterLoad?.Invoke();
            return Task.FromResult(Stored);
        }
        protected override Task SaveGoals(IMinecraftSocket socket, string userId, GoalList list)
        {
            Saves++;
            SavedUserId = userId;
            return Task.CompletedTask;
        }
        protected override Task<Dictionary<string, string>> GetNames(IMinecraftSocket socket)
        {
            NameLoads++;
            return Task.FromResult(new Dictionary<string, string> { ["HOT_RUNE"] = "Hot Rune I", ["COAL"] = "Coal" });
        }
    }

    private sealed class TestSocket : MinecraftSocket
    {
        public void SetLifecycle(ModSessionLifesycle lifecycle) => sessionLifesycle = lifecycle;
        public override T GetService<T>()
        {
            if (typeof(T) == typeof(PreApiService))
                return (T)(object)new PreApiService(null, new FlipperService(null, null), NullLogger<PreApiService>.Instance, null, null, null);
            return Moq.Mock.Of<T>();
        }
    }

    private static TestSocket CreateSocket()
    {
        var socket = new TestSocket();
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate("42"),
            AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = "42" })
        });
        socket.SessionInfo.McName = "Ekwav";
        return socket;
    }

    private static GoalList SellList(string tag)
    {
        var goal = new Goal
        {
            Id = "g", Title = tag, RootItemTag = tag,
            Root = new GoalStep { Kind = StepKind.Sell, ItemTag = tag, Quantity = 1, Children = { new GoalStep { Kind = StepKind.Craft, ItemTag = tag, Quantity = 1, DoneAt = DateTime.UtcNow } } }
        };
        return new GoalList { Goals = { goal }, FocusId = "g" };
    }

    private static Task Run(Handler handler, params string[] lines)
        => new ChatBatchCommand { GoalHandler = handler }.Execute(CreateSocket(), JsonConvert.SerializeObject(lines));

    [TestCase(-1, 64)]
    [TestCase(1, 0)]
    [TestCase(null, 0)]
    public async Task ReconnectSnapshotUsesAuthoritativeClosureToOrderClaimsBeforeSales(int? closedOffset, int expectedSellClaimed)
    {
        var socket = CreateSocket();
        var created = DateTime.UtcNow.AddMinutes(-1);
        var soldAt = created.AddSeconds(5);
        var goal = new Goal { RootItemTag = "COAL", Quantity = 64, CreatedAt = created,
            BazaarFlip = new(), Root = new GoalStep { Kind = StepKind.Sell, ItemTag = "COAL", Quantity = 64 } };
        var handler = new Handler { Stored = new GoalList { Goals = { goal } } };
        var snapshot = new Coflnet.Sky.ModCommands.Models.BazaarOrderSnapshot { UserId = socket.UserId, Revision = 1,
            Orders = new() {
                new() { ItemId = "COAL", PlayerName = "Ekwav", Amount = 64, Filled = 64, Claimed = 64,
                    IsExpired = true, IsEstimate = false, Timestamp = created.AddSeconds(1),
                    ClosedAt = closedOffset.HasValue ? soldAt.AddSeconds(closedOffset.Value) : null },
                new() { ItemId = "COAL", PlayerName = "Ekwav", IsSell = true, Amount = 64, Filled = 64, Claimed = 64,
                    IsExpired = true, IsEstimate = false, Timestamp = soldAt, ClosedAt = soldAt.AddSeconds(2) }
            } };
        var wire = JsonConvert.SerializeObject(snapshot, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        await handler.HandleOrders(socket, JsonConvert.DeserializeObject<Coflnet.Sky.ModCommands.Models.BazaarOrderSnapshot>(wire));
        Assert.That(goal.BazaarFlip.BuyClaimed, Is.EqualTo(64));
        Assert.That(goal.BazaarFlip.SellClaimed, Is.EqualTo(expectedSellClaimed));
        Assert.That(goal.BazaarFlip.Orders[0].ClosedAt, Is.EqualTo(snapshot.Orders[0].ClosedAt));
    }

    [Test]
    public async Task SnapshotIgnoresOtherOwnersItemsAndEstimatedFills()
    {
        var socket = CreateSocket();
        var created = DateTime.UtcNow.AddMinutes(-1);
        var goal = new Goal { Id = "flip", CreatedAt = created, RootItemTag = "COAL", Quantity = 64,
            BazaarFlip = new(), Root = new GoalStep { Kind = StepKind.Sell, ItemTag = "COAL", Quantity = 64 } };
        var handler = new Handler { Stored = new GoalList { Goals = { goal }, FocusId = goal.Id } };
        var snapshot = new Coflnet.Sky.ModCommands.Models.BazaarOrderSnapshot { UserId = socket.UserId, Revision = 1,
            Orders = new() {
                new() { ItemId = "COAL", PlayerName = "OtherPlayer", Amount = 64, Filled = 64, Claimed = 64, Timestamp = created.AddSeconds(1) },
                new() { ItemId = "OTHER", PlayerName = "Ekwav", Amount = 64, Filled = 64, Claimed = 64, Timestamp = created.AddSeconds(2) },
                new() { ItemId = "COAL", PlayerName = "Ekwav", Amount = 64, Filled = 64, IsEstimate = true, Timestamp = created.AddSeconds(3) }
            } };
        await handler.HandleOrders(socket, snapshot);
        Assert.That(goal.BazaarFlip.Orders, Has.Count.EqualTo(1));
        Assert.That(BazaarFlipProgress.Next(goal), Is.EqualTo(BazaarFlipAction.WaitBuy));
        snapshot.Revision = 2;
        snapshot.Orders[2].IsEstimate = false;
        snapshot.Orders[2].Claimed = 64;
        await handler.HandleOrders(socket, snapshot);
        Assert.That(goal.BazaarFlip.BuyClaimed, Is.EqualTo(64));
        Assert.That(BazaarFlipProgress.Next(goal), Is.EqualTo(BazaarFlipAction.Sell));
        var saves = handler.Saves;
        await handler.HandleOrders(socket, snapshot);
        Assert.That(handler.Saves, Is.EqualTo(saves));
    }

    [TestCase(false, 0, 0, true)]
    [TestCase(true, 32, 16, true)]
    [TestCase(true, 32, null, true)]
    [TestCase(true, 64, 64, false)]
    [TestCase(true, 0, 0, false)]
    public async Task FullSnapshotOnlyClearsGenericBuyOrdersWithoutOpenOrUnclaimedItems(bool expired, int filled, int? claimed, bool keepOrdered)
    {
        var socket = CreateSocket();
        var step = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "COAL", Quantity = 64, OrderedAt = DateTime.UtcNow };
        var handler = new Handler { Stored = new GoalList { Goals = { new Goal { Root = step } } } };
        var snapshot = new Coflnet.Sky.ModCommands.Models.BazaarOrderSnapshot { UserId = socket.UserId, Revision = 1,
            Orders = new() { new() { ItemId = "COAL", PlayerName = socket.SessionInfo.McName, Amount = 64,
                Filled = filled, Claimed = claimed, IsExpired = expired } } };
        await handler.HandleOrders(socket, snapshot);
        Assert.That(step.OrderedAt.HasValue, Is.EqualTo(keepOrdered));
        Assert.That(handler.Saves, Is.EqualTo(keepOrdered ? 0 : 1));
    }

    [Test]
    public async Task EmptyPersonalSnapshotClearsNestedBuyButPreservesSellAndCompletedSteps()
    {
        var socket = CreateSocket();
        var buy = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "COAL", OrderedAt = DateTime.UtcNow };
        var done = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "OTHER", OrderedAt = DateTime.UtcNow, DoneAt = DateTime.UtcNow };
        var root = new GoalStep { Kind = StepKind.Sell, ItemTag = "COAL", OrderedAt = DateTime.UtcNow, Children = { buy, done } };
        var handler = new Handler { Stored = new GoalList { Goals = { new Goal { Root = root } } } };
        var snapshot = new Coflnet.Sky.ModCommands.Models.BazaarOrderSnapshot { UserId = socket.UserId, Revision = 1,
            Orders = new() { new() { ItemId = "COAL", PlayerName = "OtherPlayer", Amount = 64 } } };
        await handler.HandleOrders(socket, snapshot);
        Assert.That(buy.OrderedAt, Is.Null, "Another owner's order cannot keep this user's goal ordered.");
        Assert.That(root.OrderedAt, Is.Not.Null);
        Assert.That(done.OrderedAt, Is.Not.Null);
        Assert.That(handler.Saves, Is.EqualTo(1));
    }

    [Test]
    public async Task StaleSnapshotCannotClearGenericOrder()
    {
        var socket = CreateSocket();
        var step = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = "COAL", OrderedAt = DateTime.UtcNow };
        var handler = new Handler { Stored = new GoalList { Goals = { new Goal { Root = step } } } };
        socket.SessionInfo.BazaarDisplayState = new() { Revision = 2 };
        await handler.HandleOrders(socket, new() { UserId = socket.UserId, Revision = 1 });
        Assert.That(step.OrderedAt, Is.Not.Null);
        Assert.That(handler.Saves, Is.Zero);
    }

    [Test]
    public async Task SnapshotSavesOnlyToTheAccountWhoseGoalsWereLoaded()
    {
        var userId = "42";
        var socket = new Moq.Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.UserId).Returns(() => userId);
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { McName = "Ekwav" });
        var handler = new Handler { Stored = new GoalList { Goals = { new Goal { Root = new GoalStep {
            Kind = StepKind.BuyOrder, ItemTag = "COAL", OrderedAt = DateTime.UtcNow } } } }, AfterLoad = () => userId = "43" };
        await handler.HandleOrders(socket.Object, new() { UserId = "42", Revision = 1 });
        Assert.That(handler.Saves, Is.EqualTo(1));
        Assert.That(handler.SavedUserId, Is.EqualTo("42"));
    }

    [Test]
    public async Task GuidedBazaarChatDoesNotFetchItemNamesOrCountSaleEvidence()
    {
        var handler = new Handler { Stored = SellList("COAL") };
        handler.Stored.Goals[0].BazaarFlip = new();
        await Run(handler, "[Bazaar] Sold 5x Coal for 10 coins!");
        Assert.That(handler.NameLoads, Is.Zero);
        Assert.That(handler.Saves, Is.Zero);
        Assert.That(handler.Stored.Goals[0].Root.DoneAt, Is.Null);
    }

    [Test]
    public async Task BinLine_MarksSellDoneAndSavesOnce()
    {
        var handler = new Handler { Stored = SellList("HOT_RUNE") };

        await Run(handler, "BIN Auction started for ◆ Hot Rune I!");

        Assert.That(handler.Stored.Goals[0].Root.DoneAt, Is.Not.Null);
        Assert.That(handler.Saves, Is.EqualTo(1));
    }

    [Test]
    public async Task LineForOtherItem_DoesNotSave()
    {
        var handler = new Handler { Stored = SellList("COAL") };

        await Run(handler, "BIN Auction started for ◆ Hot Rune I!");

        Assert.That(handler.Saves, Is.EqualTo(0));
    }

    [Test]
    public async Task UserWithoutGoals_DoesNotSave()
    {
        var handler = new Handler { Stored = new GoalList() };

        await Run(handler, "[Bazaar] Sold 5x Coal for 10 coins!");

        Assert.That(handler.Saves, Is.EqualTo(0));
    }

    [Test]
    public async Task FailingGoalHandling_DoesNotThrowAndNextLinesStillRun()
    {
        var handler = new Handler { Stored = SellList("HOT_RUNE"), Throw = true };
        var socket = CreateSocket();

        await new ChatBatchCommand { GoalHandler = handler }.Execute(socket, JsonConvert.SerializeObject(
            new[] { "BIN Auction started for Hot Rune I!", "Profile ID: abc-123" }));

        Assert.That(socket.SessionInfo.ProfileId, Is.EqualTo("abc-123"));
    }
}
