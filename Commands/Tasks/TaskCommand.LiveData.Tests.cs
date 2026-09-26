using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC.Tasks;

/// <summary>
/// Rendering-layer tests for the command layer (TaskDetailsCommand.BuildStepByStep/BuildDialog,
/// TaskCommand.BuildListHover) against hand-built <see cref="Coflnet.Sky.PlayerState.Client.Model.TaskResult"/>
/// DTOs - the redesign moved all task computation (registry, formula/player-data/community
/// estimate blending, MethodBreakdown/guidance population) into SkyPlayerState, where it is
/// exercised by MethodDetection.Tests.cs, StatScore.Tests.cs, TaskClassifier.Tests.cs and
/// TaskExecutionService.Tests.cs. This mod only forwards to SkyPlayerState's REST api and renders
/// the DTOs it returns, so these tests use mocked/hand-built client responses instead of executing
/// any task definition.
/// </summary>
public class TaskCommandMockDataTests
{
    // ── Clickable "buy this item" steps (see TaskDetailsCommand.BuildStepByStep) ──

    [Test]
    public void SludgeMiningGemMixture_DetailsDialog_RendersStepsAndWikiButtons()
    {
        // Shape matches what SkyPlayerState's SludgeMiningGemMixtureTask actually returns (see its
        // MethodDetection.Tests.cs) - hand-built here since the task definition itself no longer
        // lives in this mod.
        var result = TaskCommand.PrepareTaskResult(new TaskResult
        {
            Name = "Sludge Mining (Gem Mixture)",
            ProfitPerHour = 1_200_000,
            Message = "Sludge Mining (Gem Mixture) ~1.2M/h",
            Breakdown = new MethodBreakdown
            {
                Where = "Jungle",
                Island = "Crystal Hollows",
                Warp = "/warp crystals",
                WikiUrl = "https://hypixelskyblock.minecraft.wiki/w/Gemstone_Mixture",
                WhereWikiUrl = "https://hypixelskyblock.minecraft.wiki/w/Crystal_Hollows",
                Steps =
                [
                    new() { Number = 1, Text = "Type /warp crystals to get close.", OnClick = "/warp crystals" },
                    new() { Number = 2, Text = "Mine Sludge Juice with a Jungle Pickaxe in the Jungle." },
                    new() { Number = 3, Text = "Type /warp forge to get close.", OnClick = "/warp forge" },
                    new() { Number = 4, Text = "Open the Forge and craft Gemstone Mixture." },
                ]
            }
        });

        var db = DialogBuilder.New;
        TaskDetailsCommand.BuildStepByStep(db, result);
        var parts = db.Build();
        var fullText = string.Join("", parts.Select(p => p.text));

        fullText.Should().Contain("Step by step");
        fullText.Should().Contain("Jungle Pickaxe");
        fullText.Should().Contain("Forge");
        fullText.Should().Contain("[Wiki]");
        fullText.Should().Contain("[Map of Jungle]");

        parts.Should().Contain(p => p.onClick == "/warp crystals");
        parts.Should().Contain(p => p.onClick == "/warp forge");
        parts.Should().Contain(p => p.onClick != null && p.onClick.StartsWith("http"),
            "the wiki/step buttons must use http onClick so the Fabric mod opens them in the browser");
        parts.Should().Contain(p => p.onClick == "https://hypixelskyblock.minecraft.wiki/w/Gemstone_Mixture");
    }

    [Test]
    public void DetailsDialog_RequiredItemWithoutOwnClick_BecomesClickableToBuyWithPrice()
    {
        // MethodTask.BuildDefaultSteps (SkyPlayerState) emits one step per RequiredItem with no
        // OnClick of its own - rendering must enrich it with a buy click and the item's price.
        var result = TaskCommand.PrepareTaskResult(new TaskResult
        {
            Name = "Zealot's Fabled Discovery",
            Breakdown = new MethodBreakdown
            {
                RequiredItems = [new() { ItemTag = "ASPECT_OF_THE_DRAGON", Name = "Aspect of the Dragon", EstimatedPrice = 42_000_000 }],
                Steps = [new() { Number = 1, Text = "Get Aspect of the Dragon first." }]
            }
        });

        var rawStep = result.Breakdown.Steps.Single(s => s.Text.Contains("Aspect of the Dragon"));
        rawStep.OnClick.Should().BeNull("the generic 'Get X first' step has no OnClick of its own");

        var db = DialogBuilder.New;
        // Aspect of the Dragon isn't a bazaar product (empty bazaarTags), so it must fall back to /ahs.
        TaskDetailsCommand.BuildStepByStep(db, result, new HashSet<string>());
        var parts = db.Build();

        parts.Should().Contain(p => p.onClick == "/ahs Aspect of the Dragon",
            "a required item without its own OnClick should become clickable using the same " +
            "bazaar/ah command pattern PrimaryAction uses (Aspect of the Dragon isn't a bazaar product, so /ahs)");
        parts.Should().Contain(p => p.text != null && p.text.Contains("Aspect of the Dragon")
                && p.text.Contains(FormatProvider.FormatPriceShort(42_000_000)),
            "the step should show the item's estimated price");
    }

    [Test]
    public void BuildStepByStep_RequiredItemOnBazaar_UsesBazaarClick()
    {
        var result = new TaskResult
        {
            Breakdown = new MethodBreakdown
            {
                RequiredItems = [new() { ItemTag = "ENCHANTED_COAL", Name = "Enchanted Coal", EstimatedPrice = 1000 }],
                Steps = [new() { Number = 1, Text = "Get Enchanted Coal first." }]
            }
        };

        var db = DialogBuilder.New;
        TaskDetailsCommand.BuildStepByStep(db, result, new HashSet<string> { "ENCHANTED_COAL" });
        var parts = db.Build();

        parts.Should().Contain(p => p.onClick == "/bz Enchanted Coal",
            "an item that IS a bazaar product should get a /bz click (matching NpcCommand/HotkeyCommand's bazaar-vs-ah pattern), not /ahs");
    }

    [Test]
    public void BuildStepByStep_StepWithOwnOnClick_IsNotOverwritten()
    {
        var result = new TaskResult
        {
            Breakdown = new MethodBreakdown
            {
                RequiredItems = [new() { ItemTag = "HYPERION", Name = "Hyperion", EstimatedPrice = 500_000_000 }],
                Steps = [new() { Number = 1, Text = "Buy a Hyperion from the auction house.", OnClick = "/ahs Hyperion" }]
            }
        };

        var db = DialogBuilder.New;
        TaskDetailsCommand.BuildStepByStep(db, result, new HashSet<string>());
        var parts = db.Build();

        parts.Should().Contain(p => p.onClick == "/ahs Hyperion",
            "a step that already has its own OnClick must be left untouched");
    }

    [Test]
    public void BuildStepByStep_NoBazaarTagsGiven_LeavesStepsAsIs()
    {
        var result = new TaskResult
        {
            Breakdown = new MethodBreakdown
            {
                RequiredItems = [new() { ItemTag = "HYPERION", Name = "Hyperion", EstimatedPrice = 500_000_000 }],
                Steps = [new() { Number = 1, Text = "Get Hyperion first." }]
            }
        };

        var db = DialogBuilder.New;
        TaskDetailsCommand.BuildStepByStep(db, result); // no bazaarTags - e.g. existing callers/tests
        var parts = db.Build();

        parts.Should().NotContain(p => p.onClick != null && p.onClick.Contains("Hyperion"),
            "without bazaar data available, the step must not be guessed at");
    }

    [Test]
    public void BuildListHover_IncludesWhereAndFirstSteps()
    {
        var result = new TaskResult
        {
            Breakdown = new MethodBreakdown
            {
                Category = "Mining",
                Where = "Jungle",
                Island = "Crystal Hollows",
                Steps =
                [
                    new() { Number = 1, Text = "Get your gear." },
                    new() { Number = 2, Text = "Type /warp crystals." },
                    new() { Number = 3, Text = "Go mine." },
                    new() { Number = 4, Text = "Sell it." },
                ]
            }
        };

        var hover = TaskCommand.BuildListHover(result);

        hover.Should().Contain("Where: ");
        hover.Should().Contain("Jungle");
        hover.Should().Contain("Crystal Hollows");
        hover.Should().Contain("Get your gear.");
        hover.Should().Contain("Type /warp crystals.");
        hover.Should().Contain("Go mine.");
        hover.Should().NotContain("Sell it.", "hover should only preview the first 2-3 steps, not the full list");
        hover.Should().Contain("Click for step-by-step guide");
    }
}
