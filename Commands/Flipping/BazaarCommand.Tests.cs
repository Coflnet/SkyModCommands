using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using IItemsApi = Coflnet.Sky.Items.Client.Api.IItemsApi;
using Coflnet.Sky.Items.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class BazaarCommandTests
{
    private Mock<IFleetApi> fleet;
    private Mock<IAccountTierManager> tier;
    private PlanSocket socket;
    private GoalList goals;
    private FleetAllocateRequest lastRequest;
    private int fleetCalls;
    private Func<FleetAllocateResponse> respond;
    private BazaarCommand command;

    [SetUp]
    public void Setup()
    {
        goals = new GoalList();
        fleetCalls = 0;
        lastRequest = null;
        fleet = new Mock<IFleetApi>();
        fleet.Setup(f => f.FleetAllocatePostAsync(It.IsAny<FleetAllocateRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((FleetAllocateRequest r, int _, CancellationToken _) =>
            {
                fleetCalls++;
                lastRequest = r;
                return Task.FromResult(respond());
            });
        var items = new Mock<IItemsApi>();
        items.Setup(i => i.ItemNamesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 12).Select(i => new ItemPreview { Tag = $"TAG_{i}", Name = $"Item {i}" }).ToList());
        tier = new Mock<IAccountTierManager>();
        tier.Setup(t => t.HasAtLeast(It.IsAny<AccountTier>())).Returns(true);

        socket = new PlanSocket(new Dictionary<Type, object> { [typeof(IFleetApi)] = fleet.Object, [typeof(IItemsApi)] = items.Object });
        var lifecycle = new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate("5"),
            FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(new FlipSettings { ModSettings = new ModSettings() }),
            TierManager = tier.Object
        };
        socket.SetLifecycle(lifecycle);
        socket.SessionInfo.McUuid = "uuid-1";
        socket.SessionInfo.Purse = 9_000_000;
        respond = () => Response(Order(0));
        command = new TestCommand(this);
    }

    // ------------------------------------------------------------------ helpers

    private static OrderRecommendationDto Order(int i, string mode = "Active", string status = "Qualified", double? probability = 0.97, double? lower = 0.96, List<string> reasons = null)
        => new()
        {
            ItemTag = $"TAG_{i}", Mode = mode, OriginalQuantity = 64 + i, Amount = 64 + i, EntryPrice = 1230, ExitPrice = 1500, PricePerUnit = 1230,
            SellTaxRate = 0.01125, TaxSource = "Assumption", EstimatedNetProfit = 4200 + i, CapitalLockHorizonHours = 24,
            DeadlineSemantics = "from buy",
            Confidence = new FillConfidenceDto { Status = status, Probability = probability, LowerBound = lower, ReasonCodes = reasons }
        };

    private static FleetAllocateResponse Response(params OrderRecommendationDto[] orders) => Response("Allocated", null, orders);

    private static FleetAllocateResponse Response(string investingStatus, List<string> reasons, params OrderRecommendationDto[] orders)
        => new()
        {
            Quote = new FleetQuoteDto { QuoteId = "q1", GeneratedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(3), MarketFrameId = "mf", PolicyId = "pol", PolicyVersion = 2, ModelVersion = "m1" },
            Status = "Ok",
            Assignments = [new TraderAllocationDto { TraderId = "uuid-1", Orders = orders.ToList(), InvestingStatus = investingStatus, InvestingReasonCodes = reasons }]
        };

    private async Task Run(string args)
    {
        socket.SessionInfo.LastBazaarPlanAt = default;
        await command.Execute(socket, JsonConvert.SerializeObject(args));
    }

    private void SetMode(BazaarPlanMode mode, int hours = 0, long budget = 0)
    {
        var m = socket.Settings.ModSettings;
        m.BazaarMode = mode;
        m.BazaarInvestingHorizonHours = hours;
        m.BazaarPlanBudget = budget;
    }

    private ChatPart Line(string contains) => socket.Parts.First(p => p.text != null && p.text.Contains(contains));

    // ------------------------------------------------------------------ plan

    [Test]
    public async Task DefaultShowsPlanWithSmartDefaults()
    {
        SetMode(BazaarPlanMode.Investing, hours: 12, budget: 2_000_000);

        await Run("");

        Assert.That(lastRequest.Preview, Is.True);
        Assert.That(lastRequest.IncludeUnqualifiedInvesting, Is.True);
        Assert.That(lastRequest.InstanceId, Does.StartWith("preview:"));
        var trader = lastRequest.Traders.Single();
        Assert.That(trader.Strategies, Is.EqualTo(new[] { "Investing" }));
        Assert.That(trader.InvestingHorizonHours, Is.EqualTo(12));
        Assert.That(trader.BudgetCoins, Is.EqualTo(2_000_000));
        var line = Line("Item 0");
        Assert.That(line.text, Does.Contain("64x Item 0").And.Contain("[Active]").And.Contain("1,230").Or.Contain("1230"));
        Assert.That(line.text, Does.Contain("1.125%").And.Contain("Assumption").And.Contain("lock ≤24h"));
        Assert.That(line.onClick, Is.EqualTo("/cofl bz track q1 TAG_0"));
        Assert.That(line.hover, Does.Contain("The 24h clock starts when your BUY is placed").And.Contain("q1"));
        Assert.That(socket.Parts.Select(p => p.onClick), Does.Contain("/cofl bz active 12 2000000").And.Contain("/cofl bz both 12 2000000").And.Contain("/cofl bz investing 48 2000000"));
        Assert.That(socket.Sent, Is.Empty);
    }

    [Test]
    public async Task ModeAndHorizonArgsOverrideSettings()
    {
        SetMode(BazaarPlanMode.Active);

        await Run("both 36 5m");

        var trader = lastRequest.Traders.Single();
        Assert.That(trader.Strategies, Is.EqualTo(new[] { "Active", "Investing" }));
        Assert.That(trader.InvestingHorizonHours, Is.EqualTo(36));
        Assert.That(trader.BudgetCoins, Is.EqualTo(5_000_000));
        Assert.That(socket.SessionInfo.LastBazaarPlanArgs, Is.EqualTo(new BazaarPlanArgs(BazaarPlanMode.Both, 36, 5_000_000)));
    }

    [TestCase("investing 0")]
    [TestCase("investing 49")]
    [TestCase("both 100")]
    [TestCase("investing abc")]
    public async Task RejectsHorizonZeroAndAbove48(string args)
    {
        await Run(args);

        Assert.That(fleetCalls, Is.EqualTo(0), "an out of range horizon must never reach the planner or be replaced by a default");
        var error = socket.Parts.Single(p => p.text.StartsWith("§c"));
        Assert.That(error.text, Does.Contain("between 1 and 48"));
        Assert.That(error.onClick, Is.EqualTo($"/cofl bz {args.Split(' ')[0]}"));
    }

    [Test]
    public async Task UnknownModeShowsUsage()
    {
        await Run("sideways 24");

        Assert.That(fleetCalls, Is.EqualTo(0));
        Assert.That(Line("Usage").text, Does.Contain("[active|investing|both]"));
    }

    [Test]
    public async Task RendersQualifiedUnknownStaleLabelsHonestly()
    {
        respond = () => Response(
            Order(0, "Investing", "Qualified", 0.97, 0.96),
            Order(1, "Investing", "InsufficientEvidence", null, null),
            Order(2, "Active", "StaleInputs", null, null),
            Order(3, "Active", "SimulationOnly", null, null),
            Order(4, "Active", "NotApplicable", null, null, reasons: new() { "Why" }));

        await Run("both");

        Assert.That(Line("Item 0").hover, Does.Contain(">95% full buy+sell cycle (p 97.0%, lower bound 96.0%)"));
        var unknown = Line("Item 1").hover;
        Assert.That(unknown, Does.Contain("confidence unknown: insufficient evidence").And.Not.Contain("%").And.Not.Contain("97"));
        Assert.That(Line("Item 2").hover, Does.Contain("stale inputs"));
        Assert.That(Line("Item 3").hover, Does.Contain("simulation-only estimate"));
        var notApplicable = Line("Item 4").hover;
        Assert.That(notApplicable, Does.Not.Contain("confidence").And.Not.Contain("%"));
        Assert.That(notApplicable, Does.Contain("reasons: Why"));
    }

    [Test]
    public async Task EmptyQualifiedDiffersFromUnavailable()
    {
        respond = () => Response("EmptyQualified", ["NoQualifiedCandidates"]);
        await Run("investing 24");
        var empty = socket.Parts.Select(p => p.text).First(t => t.Contains("No qualified investing opportunities"));
        Assert.That(empty, Does.Contain("within 24h").And.Contain("NoQualifiedCandidates"));
        Assert.That(socket.Parts.Select(p => p.onClick), Does.Contain("/cofl bz both 24"), "toggles stay available");

        socket.Parts.Clear();
        respond = () => Response("Unavailable", ["InvestingCandidatesNotReady"]);
        await Run("investing 24");
        var unavailable = socket.Parts.Select(p => p.text).First(t => t.Contains("unavailable"));
        Assert.That(unavailable, Does.Contain("InvestingCandidatesNotReady"));
        Assert.That(socket.Parts.Select(p => p.text), Has.None.Contain("No qualified"));
    }

    [Test]
    public async Task UnqualifiedCandidatesAreSeparatedAndNotClickable()
    {
        respond = () =>
        {
            var r = Response("EmptyQualified", ["NoQualifiedCandidates"]);
            r.Assignments[0].UnqualifiedInvestingCandidates = [Order(7, "Investing", "InsufficientEvidence", null, null)];
            return r;
        };

        await Run("investing 24");

        var texts = socket.Parts.Select(p => p.text).ToList();
        Assert.That(texts.IndexOf("§7not qualified (no probability claim)"), Is.GreaterThanOrEqualTo(0));
        var candidate = Line("Item 7");
        Assert.That(candidate.onClick, Is.Null);
        Assert.That(candidate.hover, Does.Contain("not drafted"));
    }

    [Test]
    public async Task PlannerUnavailableFallsBackToLabelledSpreadList()
    {
        fleet.Setup(f => f.FleetAllocatePostAsync(It.IsAny<FleetAllocateRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("engine down"));
        var flipper = new Mock<IBazaarFlipperApi>();
        flipper.Setup(f => f.FlipsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BazaarFlip> { new() { ItemTag = "TAG_1", BuyPrice = 100, SellPrice = 120, MedianBuyPrice = 100, Volume = 1000, ProfitPerHour = 50 } });
        socket.Services[typeof(IBazaarFlipperApi)] = flipper.Object;

        await Run("active");

        var texts = socket.Parts.Select(p => p.text).ToList();
        Assert.That(texts, Has.Some.EqualTo("§cplanner unavailable, showing market spread"));
        Assert.That(texts.Any(t => t.Contains("Top Bazaar Flips")), Is.True);
        Assert.That(texts.Any(t => t.Contains("Item 1")), Is.True);
        Assert.That(socket.SessionInfo.LastBazaarPlan, Is.Null);
    }

    [Test]
    public async Task InvestingOnlyPlannerFailureShowsUnavailableNotSpreadList()
    {
        fleet.Setup(f => f.FleetAllocatePostAsync(It.IsAny<FleetAllocateRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("engine down"));
        var flipper = new Mock<IBazaarFlipperApi>();
        flipper.Setup(f => f.FlipsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BazaarFlip> { new() { ItemTag = "TAG_1", BuyPrice = 100, SellPrice = 120, MedianBuyPrice = 100, Volume = 1000, ProfitPerHour = 50 } });
        socket.Services[typeof(IBazaarFlipperApi)] = flipper.Object;

        await Run("investing 24");

        var texts = socket.Parts.Select(p => p.text).ToList();
        var unavailable = Line("Investing plan unavailable right now (planner error); inputs kept");
        Assert.That(unavailable.onClick, Is.EqualTo("/cofl bz investing 24"), "retry click repeats the inputs");
        Assert.That(texts.Any(t => t.Contains("Top Bazaar Flips") || t.Contains("market spread")), Is.False);
        flipper.Verify(f => f.FlipsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task FreeTierTrackOnHiddenTagIsRejected()
    {
        tier.Setup(t => t.HasAtLeast(It.IsAny<AccountTier>())).Returns(false);
        respond = () => Response(Enumerable.Range(0, 10).Select(i => Order(i)).ToArray());
        await Run("");
        var shown = socket.Parts.Where(p => p.onClick?.StartsWith("/cofl bz track") == true).Select(p => p.onClick).ToList();
        var hiddenTag = Enumerable.Range(0, 10).Select(i => $"TAG_{i}").First(t => !shown.Contains($"/cofl bz track q1 {t}"));
        socket.Parts.Clear();
        socket.Sent.Clear();

        await Run($"track q1 {hiddenTag}");

        Assert.That(goals.Goals, Is.Empty);
        Assert.That(socket.Parts.Select(p => p.text), Has.Some.Contain("not part of your latest plan"));
        Assert.That(socket.Sent, Is.Empty);
    }

    [Test]
    public async Task FreeTierShowsThreeSamplesFromRanks4To10StablePerUser()
    {
        tier.Setup(t => t.HasAtLeast(It.IsAny<AccountTier>())).Returns(false);
        respond = () => Response(Enumerable.Range(0, 10).Select(i => Order(i)).ToArray());

        await Run("");
        var first = socket.Parts.Where(p => p.onClick?.StartsWith("/cofl bz track") == true).Select(p => p.onClick).ToList();
        socket.Parts.Clear();
        await Run("");
        var second = socket.Parts.Where(p => p.onClick?.StartsWith("/cofl bz track") == true).Select(p => p.onClick).ToList();

        Assert.That(first, Has.Count.EqualTo(3));
        Assert.That(second, Is.EqualTo(first));
        var ranks = first.Select(c => int.Parse(c.Split('_').Last()) + 1).ToList();
        Assert.That(ranks, Has.All.InRange(4, 10));
        Assert.That(ranks.Distinct().Count(), Is.EqualTo(3));
        // the entries that are not shown never appear anywhere in the output
        var hidden = Enumerable.Range(0, 10).Where(i => !first.Contains($"/cofl bz track q1 TAG_{i}")).ToList();
        Assert.That(socket.Parts.Select(p => (p.text ?? "") + (p.hover ?? "") + (p.onClick ?? "")),
            Has.None.Matches<string>(t => hidden.Any(i => t.Contains($"Item {i} ") || t.Contains($"TAG_{i}"))));
    }

    [Test]
    public async Task FreeTierUpsellLineHasNoHoverOrClick()
    {
        tier.Setup(t => t.HasAtLeast(It.IsAny<AccountTier>())).Returns(false);
        respond = () => Response(Enumerable.Range(0, 10).Select(i => Order(i)).ToArray());

        await Run("");

        var upsell = Line("more recommendations with starter premium");
        Assert.That(upsell.text, Does.Contain("7 more recommendations"));
        Assert.That(upsell.hover, Is.Null);
        Assert.That(upsell.onClick, Is.Null);
    }

    [Test]
    public async Task FreeTierSearchStillCoversAllEntriesWithTopTwoHidden()
    {
        tier.Setup(t => t.HasAtLeast(It.IsAny<AccountTier>())).Returns(false);
        fleet.Setup(f => f.FleetAllocatePostAsync(It.IsAny<FleetAllocateRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));
        var flipper = new Mock<IBazaarFlipperApi>();
        flipper.Setup(f => f.FlipsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 6).Select(i => new BazaarFlip { ItemTag = $"TAG_{i}", BuyPrice = 100, SellPrice = 120, MedianBuyPrice = 100, Volume = 1000, ProfitPerHour = 1000 - i }).ToList());
        socket.Services[typeof(IBazaarFlipperApi)] = flipper.Object;
        socket.SessionInfo.Purse = 1_000_000;
        socket.SessionInfo.LastBazaarPlanAt = default;

        // search term on the legacy list ("item" matches every row)
        await command.Execute(socket, "\"item\"");
        var rows = socket.Parts.Where(p => p.text != null && (p.text.Contains("Item ") || p.text.Contains("requires at least starter premium"))).ToList();

        Assert.That(rows.Count(r => r.text.Contains("Item ")), Is.EqualTo(4), "all but the top 2 stay visible");
        var hiddenRows = rows.Where(r => r.text.Contains("requires at least starter premium")).ToList();
        Assert.That(hiddenRows, Has.Count.EqualTo(2));
        Assert.That(hiddenRows.Select(r => r.hover), Has.All.Null);
        Assert.That(hiddenRows.Select(r => r.onClick), Has.All.Null);
    }

    [Test]
    public async Task HistoryAndListSubcommandsUnchanged()
    {
        var uuid = Guid.NewGuid();
        socket.SessionInfo.MinecraftUuids.Add(uuid.ToString());
        socket.Lifecycle.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = "5", McIds = new() });
        var profit = new Mock<IBazaarProfitApi>();
        profit.Setup(p => p.BazaarProfitSummaryPlayerUuidGetAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BazaarProfitSummary { TotalProfit = 1000, FlipCount = 3, OutstandingValue = 5 });
        profit.Setup(p => p.BazaarProfitFlipsPlayerUuidGetAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CompletedBazaarFlip>());
        socket.Services[typeof(IBazaarProfitApi)] = profit.Object;

        await Run("history");
        Assert.That(socket.Parts.Select(p => p.text), Has.Some.Contain("Bazaar Profit History"));
        socket.Parts.Clear();
        await Run("l 3");
        Assert.That(socket.Parts.Select(p => p.text), Has.Some.Contain("No completed flips in the last 3 days"));
        Assert.That(fleetCalls, Is.EqualTo(0));
    }

    // ------------------------------------------------------------------ track

    [Test]
    public async Task TrackCreatesPlanGoalFocusesItAndSendsExpectCommand()
    {
        respond = () => Response(Order(0, "Investing", "Qualified", 0.97, 0.96));
        await Run("investing 24");

        await Run("track q1 tag_0");

        var goal = goals.Goals.Single();
        Assert.That(goals.FocusId, Is.EqualTo(goal.Id));
        Assert.That(goal.RootItemTag, Is.EqualTo("TAG_0"));
        Assert.That(goal.Quantity, Is.EqualTo(64));
        Assert.That(goal.Source, Is.EqualTo("mod"));
        Assert.That(goal.BazaarFlip, Is.Not.Null);
        Assert.That(goal.Root.Kind, Is.EqualTo(StepKind.Sell));
        Assert.That(goal.CreatedAt.Ticks % TimeSpan.TicksPerMillisecond, Is.EqualTo(0));
        Assert.That(goal.Plan, Has.Property("QuoteId").EqualTo("q1").And.Property("Mode").EqualTo("Investing")
            .And.Property("EntryPrice").EqualTo(1230).And.Property("ExitPrice").EqualTo(1500).And.Property("HorizonHours").EqualTo(24)
            .And.Property("ConfidenceStatus").EqualTo("Qualified").And.Property("Probability").EqualTo(0.97));
        Assert.That(goal.Plan.Deadline, Is.Null, "the deadline starts with the first observed buy");
        var step = Line("Place buy order");
        Assert.That(step.onClick, Does.StartWith("/cofl bzorder"));
        var expect = socket.Sent.Single(r => r.type == "expect");
        Assert.That(expect.data, Does.Contain(step.onClick.Replace("/", "\\/")).Or.Contain(step.onClick));
        Assert.That(expect.data, Does.Contain("120"));
        Assert.That(socket.Sent.Select(r => r.type), Has.None.EqualTo("placeOrder"));
    }

    [Test]
    public async Task TrackRejectsExpiredQuote()
    {
        await Run("");
        socket.SessionInfo.LastBazaarPlan.Quote.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        var callsBefore = fleetCalls;
        respond = () => Response("Allocated", null);

        await Run("track q1 TAG_0");

        Assert.That(goals.Goals, Is.Empty);
        Assert.That(fleetCalls, Is.EqualTo(callsBefore + 1), "an expired quote is re-planned, never tracked");
        Assert.That(socket.Parts.Select(p => p.text), Has.Some.Contain("TAG_0 is no longer recommended"));
        Assert.That(socket.Sent, Is.Empty);
    }

    [Test]
    public async Task TrackRejectsSecondBuyForSameItem()
    {
        await Run("");
        await Run("track q1 TAG_0");
        Assert.That(goals.Goals, Has.Count.EqualTo(1));
        socket.Parts.Clear();
        socket.Sent.Clear();

        await Run("track q1 TAG_0");

        Assert.That(goals.Goals, Has.Count.EqualTo(1));
        Assert.That(socket.Parts.Select(p => p.text), Has.Some.Contain("already have an active Bazaar flip"));
        Assert.That(socket.Sent, Is.Empty);
    }

    [Test]
    public async Task NeverSendsPlaceOrder()
    {
        respond = () => Response(Order(0), Order(1, "Investing"));
        // the history sub command needs an account and a profit api; provide them so the path really runs
        socket.SessionInfo.MinecraftUuids.Add(Guid.NewGuid().ToString());
        socket.Lifecycle.AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = "5", McIds = new() });
        var profit = new Mock<IBazaarProfitApi>();
        profit.Setup(p => p.BazaarProfitSummaryPlayerUuidGetAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BazaarProfitSummary { TotalProfit = 1000, FlipCount = 3, OutstandingValue = 5 });
        profit.Setup(p => p.BazaarProfitFlipsPlayerUuidGetAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CompletedBazaarFlip>());
        socket.Services[typeof(IBazaarProfitApi)] = profit.Object;
        var flipper = new Mock<IBazaarFlipperApi>();
        flipper.Setup(f => f.FlipsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BazaarFlip> { new() { ItemTag = "TAG_1", BuyPrice = 100, SellPrice = 120, MedianBuyPrice = 100, Volume = 1000, ProfitPerHour = 50 } });
        socket.Services[typeof(IBazaarFlipperApi)] = flipper.Object;
        foreach (var args in new[] { "", "both 12", "investing 0", "track q1 TAG_0", "track q1 TAG_1", "track q1 TAG_0", "track nope TAG_1", "h" })
        {
            await Run(args); // any exception fails the test: every path must actually run to completion
        }
        fleet.Setup(f => f.FleetAllocatePostAsync(It.IsAny<FleetAllocateRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("down"));
        await Run("");

        Assert.That(socket.Sent.Select(r => r.type), Has.None.EqualTo("placeOrder"));
        Assert.That(socket.Executed, Is.Empty);
    }

    // ------------------------------------------------------------------ test doubles

    private class TestCommand(BazaarCommandTests t) : BazaarCommand
    {
        protected override Task<GoalList> LoadGoals(MinecraftSocket socket, string userId) => Task.FromResult(t.goals);
        protected override Task SaveGoals(MinecraftSocket socket, string userId, GoalList list)
        {
            t.goals = list;
            return Task.CompletedTask;
        }
    }

    private class PlanSocket(Dictionary<Type, object> services) : MinecraftSocket
    {
        public Dictionary<Type, object> Services { get; } = services;
        public List<ChatPart> Parts { get; } = new();
        public List<Response> Sent { get; } = new();
        public List<string> Executed { get; } = new();
        public ModSessionLifesycle Lifecycle => sessionLifesycle;
        public void SetLifecycle(ModSessionLifesycle l) => sessionLifesycle = l;
        public override T GetService<T>() => Services.TryGetValue(typeof(T), out var s) ? (T)s : null;
        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation) => Parts.AddRange(creation(new SocketDialogBuilder(this)).Build());
        public override bool SendMessage(params ChatPart[] parts)
        {
            Parts.AddRange(parts);
            return true;
        }
        public override void Send(Response response) => Sent.Add(response);
        public override void ExecuteCommand(string command) => Executed.Add(command);
    }
}
