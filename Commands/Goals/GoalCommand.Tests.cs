using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class GoalCommandTests
{
    private GoalList stored;
    private TestSocket socket;
    private GoalCommand command;
    private Mock<ICraftsApi> crafts;

    [SetUp]
    public void Setup()
    {
        stored = new GoalList
        {
            Goals = new()
            {
                new Goal { Id = "a1", Title = "Alpha", Root = new GoalStep { Kind = StepKind.Craft, ItemTag = "A", Quantity = 1 } },
                new Goal { Id = "b2", Title = "Beta", Root = new GoalStep { Kind = StepKind.Craft, ItemTag = "B", Quantity = 1 } }
            },
            FocusId = "b2"
        };
        crafts = new Mock<ICraftsApi>();
        socket = new TestSocket(crafts.Object);
        socket.SetLifecycle(new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate("5")
        });
        command = new TestCommand(this);
    }

    [Test]
    public async Task ListMarksFocusedAndIsClickable()
    {
        await command.Execute(socket, "\"list\"");

        var lines = socket.Parts;
        var alpha = lines.Single(p => p.text.Contains("Alpha"));
        var beta = lines.Single(p => p.text.Contains("Beta"));
        Assert.That(alpha.text, Does.StartWith("§7▸ Alpha "));
        Assert.That(alpha.text, Does.Match(@"\d+/\d+"));
        Assert.That(beta.text, Does.StartWith("§6★ Beta"));
        Assert.That(alpha.onClick, Is.EqualTo("/cofl goal focus a1"));
        Assert.That(alpha.hover, Does.Contain("remove a1").And.Contain("reset a1"));
    }

    [Test]
    public async Task FocusRemoveAndClearMutateList()
    {
        await command.Execute(socket, "\"focus a1\"");
        Assert.That(stored.FocusId, Is.EqualTo("a1"));

        await command.Execute(socket, "\"remove a1\"");
        Assert.That(stored.Goals.Select(g => g.Id), Is.EqualTo(new[] { "b2" }));
        Assert.That(stored.FocusId, Is.EqualTo("b2"));

        await command.Execute(socket, "\"clear\"");
        Assert.That(stored.Goals, Is.Empty);
        Assert.That(stored.FocusId, Is.Null);
    }

    [Test]
    public async Task AddBuildsThroughCraftsApi()
    {
        stored = new GoalList();
        crafts.Setup(c => c.GetAcquisitionAsync("ENCHANTED_DIAMOND", 2, true, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CraftAcquisitionPlan());

        await command.Execute(socket, "\"add enchanted_diamond 2\"");

        crafts.Verify(c => c.GetAcquisitionAsync("ENCHANTED_DIAMOND", 2, true, 0, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(stored.Goals, Has.Count.EqualTo(1));
        Assert.That(stored.Goals[0].RootItemTag, Is.EqualTo("ENCHANTED_DIAMOND"));
        Assert.That(stored.Goals[0].Quantity, Is.EqualTo(2));
        Assert.That(stored.FocusId, Is.EqualTo(stored.Goals[0].Id));
        Assert.That(socket.Parts.Select(p => p.text), Has.Some.Contains("Tracking"));
    }

    private class TestCommand(GoalCommandTests t) : GoalCommand
    {
        protected override Task<GoalList> LoadGoals(MinecraftSocket socket, string userId) => Task.FromResult(t.stored);
        protected override Task SaveGoals(MinecraftSocket socket, string userId, GoalList list)
        {
            t.stored = list;
            return Task.CompletedTask;
        }
        protected override Task<ISet<string>> GetBazaarTags(MinecraftSocket socket) => Task.FromResult<ISet<string>>(new HashSet<string>());
        protected override Task<Dictionary<string, string>> GetNames(MinecraftSocket socket) => Task.FromResult(new Dictionary<string, string>());
    }

    private class TestSocket(ICraftsApi crafts) : MinecraftSocket
    {
        public List<ChatPart> Parts { get; } = new();
        public void SetLifecycle(ModSessionLifesycle l) => sessionLifesycle = l;
        public override T GetService<T>() => crafts as T ?? Mock.Of<T>();
        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            Parts.AddRange(creation(new SocketDialogBuilder(this)).Build());
        }
    }
}
