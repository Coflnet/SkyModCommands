using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.PlayerState.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Covers TaskCommand.PrepareTaskResult itself (the command layer's own rewrite of a task's
/// OnClick to the taskdetails drill-down) - the underlying task definitions are covered by
/// SkyUserState's own MethodDetection.Tests.cs, which this used to duplicate.
/// </summary>
public class TaskCommandTests
{
    [Test]
    public void PrepareTaskResult_RewritesClickToDetails_AndPreservesPrimaryAction()
    {
        var result = TaskCommand.PrepareTaskResult(new TaskResult
        {
            Name = "Forge",
            OnClick = "/warp forge"
        });

        result.OnClick.Should().Be("/cofl taskdetails Forge");
        result.PrimaryAction.Should().Be("/warp forge");
    }

    private static TaskResult Task(string name, string category, string island = null, string where = null)
    {
        return new TaskResult
        {
            Name = name,
            Message = name + " message",
            Breakdown = new MethodBreakdown { Category = category, Island = island, Where = where }
        };
    }

    private static readonly TaskResult[] Sample = new[]
    {
        Task("Wheat", "Farming", "Garden", "Plot"),
        Task("Carrots", "Farming", "Garden", "Plot"),
        Task("Zombies", "Mob Farming", "Hub", "Graveyard"),
        Task("Pest control", "Gardening", "Garden", "Barn"),
        Task("Gemstones", "Mining", "Galatea", "Lush Meadow"),
    };

    private static string[] Names(string query) =>
        TaskCommand.SearchTasks(Sample, query, e => e.ProfitPerHour + e.Message).Select(e => e.Name).ToArray();

    [Test]
    public void Search_ExactCategoryOnlyReturnsThatCategory()
    {
        Names("Farming").Should().Equal("Wheat", "Carrots");
        Names(" farming ").Should().Equal("Wheat", "Carrots");
    }

    [Test]
    public void Search_MultiWordCategoryOnlyReturnsMobFarming()
    {
        Names("mob farming").Should().Equal("Zombies");
    }

    [Test]
    public void Search_FreeTextMatchesIslandWhereAndName()
    {
        Names("galatea").Should().Equal("Gemstones");
        Names("lush").Should().Equal("Gemstones");
        Names("pest").Should().Equal("Pest control");
        Names("Garden").Should().Equal("Wheat", "Carrots", "Pest control");
    }

    [Test]
    public void Search_DoesNotMatchAcrossFields()
    {
        Names("farming garden").Should().BeEmpty();
    }

    [Test]
    public void Search_HandlesMissingBreakdown()
    {
        TaskCommand.SearchTasks(new[] { new TaskResult { Name = "Solo" } }, "solo", e => "id").Should().HaveCount(1);
    }

    [Test]
    public void Categories_CountAllElementsOrderedByCount_SkippingEmpty()
    {
        var all = new[]
        {
            Task("a", "Fishing"), Task("b", "Mining"), Task("c", "Mining"),
            Task("d", null), Task("e", ""), new TaskResult { Name = "f" }
        };

        var counts = TaskCommand.CountCategories(all);

        counts.Should().Equal(("Mining", 2), ("Fishing", 1));
    }

    [Test]
    public void CategorySummary_IsOneLineWithClickableEntries()
    {
        var parts = new Coflnet.Sky.ModCommands.Dialogs.DialogBuilder();
        TaskCommand.AddCategorySummary(parts, new[] { Task("a", "Mining"), Task("b", "Mining"), Task("c", "Fishing") });

        var built = parts.Build();
        built.Where(p => p.onClick == "/cofl task Mining").Should().ContainSingle().Which.text.Should().Contain("(2)");
        built.Any(p => p.onClick == "/cofl task Fishing").Should().BeTrue();
        built.Count(p => p.text == "\n").Should().Be(1);
    }

    [Test]
    public void CategorySummary_UsesUnfilteredElementsAfterCategoryFilter()
    {
        var filtered = TaskCommand.SearchTasks(Sample, "Farming", e => "").ToList();
        var db = new Coflnet.Sky.ModCommands.Dialogs.DialogBuilder();
        TaskCommand.AddCategorySummary(db, Sample);

        filtered.Should().HaveCount(2);
        db.Build().Count(p => p.onClick?.StartsWith("/cofl task ") == true).Should().Be(4);
    }

    [Test]
    public void CategorySummary_AddsNothingWithoutCategories()
    {
        var db = new Coflnet.Sky.ModCommands.Dialogs.DialogBuilder();
        TaskCommand.AddCategorySummary(db, new[] { Task("a", null) });

        db.Build().Should().HaveCount(1);
    }

    [Test]
    public void Title_IsProfitableTasks()
    {
        new TitleProbe().ReadTitle().Should().Be("Profitable tasks");
    }

    private class TitleProbe : TaskCommand
    {
        public string ReadTitle() => Title;
    }
}
