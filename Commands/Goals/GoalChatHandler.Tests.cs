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
        public bool Throw;

        protected override Task<GoalList> LoadGoals(MinecraftSocket socket, string userId)
            => Throw ? throw new InvalidOperationException("settings down") : Task.FromResult(Stored);
        protected override Task SaveGoals(MinecraftSocket socket, string userId, GoalList list)
        {
            Saves++;
            return Task.CompletedTask;
        }
        protected override Task<Dictionary<string, string>> GetNames(MinecraftSocket socket)
            => Task.FromResult(new Dictionary<string, string> { ["HOT_RUNE"] = "Hot Rune I", ["COAL"] = "Coal" });
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
