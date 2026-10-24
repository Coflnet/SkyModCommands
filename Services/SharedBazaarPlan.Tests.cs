using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Tests for the bazaar plan additions of the shared SkyBackendForFrontend library
/// (that library excludes *.Tests.cs, so they live here where it is compiled by path).
/// </summary>
public class SharedBazaarPlanTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static FlipSettings Settings() => new() { ModSettings = new ModSettings() };

    private static BazaarPlanInfo PlanInfo(double horizon = 24, DateTime? deadline = null) => new()
    {
        Mode = "Investing", OriginalQuantity = 64, EntryPrice = 1230, ExitPrice = 1500, SellTaxRate = 0.0125, TaxSource = "bazaar",
        EstimatedNetProfit = 4200, HorizonHours = horizon, Deadline = deadline, ConfidenceStatus = "Qualified", Probability = 0.97, LowerBound = 0.96
    };

    private static Goal PlanGoal(BazaarPlanInfo plan = null, bool withPlan = true) => new()
    {
        Title = "t", RootItemTag = "ENCHANTED_DIAMOND", Quantity = 64, CreatedAt = T0,
        Root = new GoalStep { Id = "root" }, BazaarFlip = new BazaarFlipState(),
        Plan = withPlan ? plan ?? PlanInfo() : null
    };

    private static BazaarFlipOrder Buy(DateTime at, long amount = 64, long filled = 0, bool active = true)
        => new() { CreatedAt = at, Amount = amount, Filled = filled, Active = active };

    // ---------- SettingsUpdater ----------

    [Test]
    public void BazaarModeParsesCaseInsensitive()
    {
        var s = Settings();
        new SettingsUpdater().Update(s, "modbazaarMode", "investing");
        Assert.That(s.ModSettings.BazaarMode, Is.EqualTo(BazaarPlanMode.Investing));
        new SettingsUpdater().Update(s, "bzmode", "BOTH");
        Assert.That(s.ModSettings.BazaarMode, Is.EqualTo(BazaarPlanMode.Both));
    }

    [Test]
    public void BazaarHorizonRejectsZeroIsAllowedAsDefault()
    {
        var s = Settings();
        s.ModSettings.BazaarInvestingHorizonHours = 12;
        new SettingsUpdater().Update(s, "modbazaarHorizonHours", "0");
        Assert.That(s.ModSettings.BazaarInvestingHorizonHours, Is.EqualTo(0));
    }

    [TestCase("49")]
    [TestCase("100")]
    public void BazaarHorizonRejectsAbove48(string value) => AssertHorizonRejected(value);

    [Test]
    public void BazaarHorizonRejectsNegative() => AssertHorizonRejected("-1");

    private static void AssertHorizonRejected(string value)
    {
        var s = Settings();
        s.ModSettings.BazaarInvestingHorizonHours = 12;
        var ex = Assert.Throws<CoflnetException>(() => new SettingsUpdater().Update(s, "modbazaarHorizonHours", value));
        Assert.That(ex.Slug, Is.EqualTo("invalid_horizon"));
        Assert.That(s.ModSettings.BazaarInvestingHorizonHours, Is.EqualTo(12), "previous value restored");
    }

    [Test]
    [TestCase("0.4")]
    [TestCase("48.4")]
    public void BazaarHorizonRejectsFractionalInput(string value) => AssertHorizonRejected(value);

    [Test]
    public void BazaarModeRejectsUndefinedNumber()
    {
        var s = Settings();
        s.ModSettings.BazaarMode = BazaarPlanMode.Investing;
        var ex = Assert.Throws<CoflnetException>(() => new SettingsUpdater().Update(s, "bzmode", "7"));
        Assert.That(ex.Slug, Is.EqualTo("invalid_mode"));
        Assert.That(s.ModSettings.BazaarMode, Is.EqualTo(BazaarPlanMode.Investing));
    }

    [Test]
    public void BazaarHorizonAccepts48()
    {
        var s = Settings();
        new SettingsUpdater().Update(s, "modbazaarHorizonHours", "48");
        Assert.That(s.ModSettings.BazaarInvestingHorizonHours, Is.EqualTo(48));
        new SettingsUpdater().Update(s, "modbazaarHorizonHours", "1");
        Assert.That(s.ModSettings.BazaarInvestingHorizonHours, Is.EqualTo(1));
    }

    [Test]
    public void BazaarBudgetRejectsNegative()
    {
        var s = Settings();
        s.ModSettings.BazaarPlanBudget = 5000;
        var ex = Assert.Throws<CoflnetException>(() => new SettingsUpdater().Update(s, "modbazaarBudget", "-5"));
        Assert.That(ex.Slug, Is.EqualTo("invalid_budget"));
        Assert.That(s.ModSettings.BazaarPlanBudget, Is.EqualTo(5000));
        new SettingsUpdater().Update(s, "modbazaarBudget", "2m");
        Assert.That(s.ModSettings.BazaarPlanBudget, Is.EqualTo(2_000_000));
    }

    [Test]
    public void OptionsListContainsModBazaarKeys()
    {
        var options = new SettingsUpdater().Options();
        Console.WriteLine(string.Join(",", options.Where(o => o.Contains("bazaar", StringComparison.OrdinalIgnoreCase) || o.StartsWith("bz"))));
        Assert.That(options, Does.Contain("modbazaarMode"));
        Assert.That(options, Does.Contain("modbazaarHorizonHours"));
        Assert.That(options, Does.Contain("modbazaarBudget"));
    }

    [Test]
    public void LegacyModSettingsJsonDeserializesToActiveZeroZero()
    {
        var mod = JsonConvert.DeserializeObject<ModSettings>("{\"hotkeys\":{},\"tempBlacklistThreshold\":20}");
        Assert.That(mod.BazaarMode, Is.EqualTo(BazaarPlanMode.Active));
        Assert.That(mod.BazaarInvestingHorizonHours, Is.EqualTo(0));
        Assert.That(mod.BazaarPlanBudget, Is.EqualTo(0));
    }

    // ---------- BazaarFlipProgress ----------

    [Test]
    public void PlanDeadlineStartsAtFirstObservedBuyAndNeverResets()
    {
        var goal = PlanGoal(PlanInfo(horizon: 24));
        Assert.That(BazaarFlipProgress.Observe(goal, Array.Empty<BazaarFlipOrder>(), 1), Is.False);
        Assert.That(goal.Plan.Deadline, Is.Null);
        var first = T0.AddMinutes(5);
        Assert.That(BazaarFlipProgress.Observe(goal, new[] { Buy(first) }, 2), Is.True);
        Assert.That(goal.Plan.Deadline, Is.EqualTo(first.AddHours(24)));
        // cancel and replace with a later buy
        var replacement = T0.AddHours(3);
        BazaarFlipProgress.Observe(goal, new[] { Buy(first, active: false), Buy(replacement) }, 3);
        Assert.That(goal.Plan.Deadline, Is.EqualTo(first.AddHours(24)));
    }

    [Test]
    public void PlanDeadlineIgnoresBuyThatDoesNotMatchQuantityOrPrice()
    {
        var goal = PlanGoal(PlanInfo(horizon: 24));
        // stored orders carry no price, so a different quantity is what can be told apart
        BazaarFlipProgress.Observe(goal, new[] { Buy(T0.AddMinutes(1), amount: 10) }, 1);
        Assert.That(goal.Plan.Deadline, Is.Null);
        var match = T0.AddMinutes(9);
        BazaarFlipProgress.Observe(goal, new[] { Buy(T0.AddMinutes(1), amount: 10), Buy(match) }, 2);
        Assert.That(goal.Plan.Deadline, Is.EqualTo(match.AddHours(24)));
    }

    [Test]
    public void PlanLineShowsEntryPriceNetDeadlineAndKeepsBzOrderCommand()
    {
        var goal = PlanGoal();
        var line = BazaarFlipProgress.Line(goal, true, "Enchanted Diamond", "ench diamond", T0);
        Assert.That(line.Text, Is.EqualTo("§aPlace buy order: 64x Enchanted Diamond §7@ §61.23k"));
        Assert.That(line.OnClick, Is.EqualTo("/cofl bzorder 64 ench diamond"));
        Assert.That(line.Hover, Does.Contain("net est. +4.2k after 1.25% tax (bazaar)"));
        Assert.That(line.Hover, Does.Contain("deadline starts with your first buy"));
        Assert.That(line.Hover, Does.Contain(">95% chance to complete the full buy+sell cycle by the deadline (lower bound 96%)"));
        goal.Plan.Deadline = new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc);
        Assert.That(BazaarFlipProgress.Line(goal, true, "x", "x", T0).Hover, Does.Contain("deadline 2026-10-25 09:30 UTC"));
    }

    [Test]
    public void PlanLinePastDeadlineIsInformativeOnly()
    {
        var goal = PlanGoal(PlanInfo(deadline: T0.AddHours(1)));
        BazaarFlipProgress.Observe(goal, new[] { Buy(T0.AddMinutes(1)) }, 1);
        var line = BazaarFlipProgress.Line(goal, true, "Item", "item", T0.AddHours(2));
        Assert.That(BazaarFlipProgress.Next(goal), Is.EqualTo(BazaarFlipAction.WaitBuy));
        Assert.That(line.Text, Does.StartWith("§cdeadline passed · "));
        Assert.That(line.OnClick, Is.EqualTo("/managebazaarorders"));
        Assert.That(line.Text, Does.Not.Contain("cancel").IgnoreCase);
        var before = BazaarFlipProgress.Line(goal, true, "Item", "item", T0);
        Assert.That(before.Text, Does.Not.Contain("deadline passed"));
    }

    [Test]
    public void LegacyGoalLineUnchanged()
    {
        var goal = PlanGoal(withPlan: false);
        var line = BazaarFlipProgress.Line(goal, true, "Item", "item", T0);
        Assert.That(line.Text, Is.EqualTo("§aPlace buy order: 64x Item"));
        Assert.That(line.OnClick, Is.EqualTo("/cofl bzorder 64 item"));
        Assert.That(line.Hover, Is.EqualTo("Choose Create Buy Order and review the current price before confirming."));
        BazaarFlipProgress.Observe(goal, new[] { Buy(T0.AddMinutes(1)) }, 1);
        line = BazaarFlipProgress.Line(goal, false, "Item", "item", T0.AddDays(9));
        Assert.That(line.Text, Is.EqualTo("§eWaiting for buy fills: 0/64x Item"));
        Assert.That(line.OnClick, Is.Null);
        Assert.That(line.Hover, Is.EqualTo("Open Your Bazaar Orders to inspect or claim this item. Cancelling unfilled quantities allows replacement orders. Talk to a Bazaar NPC first."));
    }

    [Test]
    public void PlanWithNullProbabilityRendersUnknownNotPercent()
    {
        var plan = PlanInfo();
        plan.ConfidenceStatus = "InsufficientEvidence";
        plan.Probability = null;
        plan.LowerBound = null;
        var hover = BazaarFlipProgress.Line(PlanGoal(plan), true, "Item", "item", T0).Hover;
        Assert.That(hover, Does.Contain("confidence unknown: insufficient evidence"));
        Assert.That(hover, Does.Not.Contain("%").Or.Contain("tax"));
        Assert.That(hover.Replace("1.25% tax", ""), Does.Not.Contain("%"));
        plan.ConfidenceStatus = "SimulationOnly";
        Assert.That(BazaarFlipProgress.Line(PlanGoal(plan), true, "Item", "item", T0).Hover, Does.Contain("simulation-only estimate, not a guarantee"));
        plan.ConfidenceStatus = "StaleInputs";
        Assert.That(BazaarFlipProgress.Line(PlanGoal(plan), true, "Item", "item", T0).Hover, Does.Contain("stale quote"));
        plan.ConfidenceStatus = "Whatever";
        Assert.That(BazaarFlipProgress.Line(PlanGoal(plan), true, "Item", "item", T0).Hover, Does.Contain("no probability claim"));
    }

    // ---------- GoalProgressEvaluator ----------

    [Test]
    public void BazaarPlanActionableStepCarriesPlannedUnitPrice()
    {
        var goal = PlanGoal();
        var progress = GoalProgressEvaluator.Evaluate(goal, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), T0);
        Assert.That(progress.Actionable.Single().UnitPrice, Is.EqualTo(1230));
        goal.BazaarFlip.Orders.Add(new BazaarFlipOrder { CreatedAt = T0, Amount = 64, Filled = 64, Claimed = 64 });
        progress = GoalProgressEvaluator.Evaluate(goal, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), T0);
        Assert.That(progress.Actionable.Single().Kind, Is.EqualTo(StepKind.Sell));
        Assert.That(progress.Actionable.Single().UnitPrice, Is.EqualTo(1500));
        goal.Plan = null;
        progress = GoalProgressEvaluator.Evaluate(goal, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), T0);
        Assert.That(progress.Actionable.Single().UnitPrice, Is.Null);
    }

    // ---------- FreeTierPlanSampler ----------

    [Test]
    public void PicksThreeFromRanks4To10()
    {
        var picks = FreeTierPlanSampler.SampleIndices(30, "user", T0);
        Assert.That(picks, Has.Count.EqualTo(3));
        Assert.That(picks.Distinct().Count(), Is.EqualTo(3));
        Assert.That(picks, Is.Ordered);
        Assert.That(picks, Is.All.InRange(3, 9));
    }

    [Test]
    public void StableForSameUserAndDay()
    {
        Assert.That(FreeTierPlanSampler.SampleIndices(30, "user", T0.AddHours(3)),
            Is.EqualTo(FreeTierPlanSampler.SampleIndices(30, "user", T0.AddHours(9))));
    }

    [Test]
    public void DifferentUsersGetDifferentPicks()
    {
        var distinct = Enumerable.Range(0, 20).Select(i => string.Join(",", FreeTierPlanSampler.SampleIndices(30, "user" + i, T0))).Distinct().Count();
        Assert.That(distinct, Is.GreaterThan(1));
    }

    [Test]
    public void FewerThanFourCandidatesReturnsEmpty()
    {
        foreach (var n in new[] { 0, 1, 3 })
            Assert.That(FreeTierPlanSampler.SampleIndices(n, "user", T0), Is.Empty);
    }

    [Test]
    public void ClampsWindowWhenFewerThanTenCandidates()
    {
        Assert.That(FreeTierPlanSampler.SampleIndices(4, "u", T0), Is.EqualTo(new[] { 3 }));
        for (int i = 0; i < 20; i++)
            Assert.That(FreeTierPlanSampler.SampleIndices(6, "u" + i, T0), Is.All.InRange(3, 5));
        Assert.That(FreeTierPlanSampler.SampleIndices(6, "u", T0), Has.Count.EqualTo(3));
    }

    // ---------- BazaarPlanPreferences ----------

    [Test]
    public void BothYieldsActiveAndInvesting()
    {
        var r = BazaarPlanPreferences.Resolve(new ModSettings { BazaarMode = BazaarPlanMode.Both, BazaarInvestingHorizonHours = 6, BazaarPlanBudget = 123 });
        Assert.That(r.Strategies, Is.EqualTo(new[] { "Active", "Investing" }));
        Assert.That(r.InvestingHorizonHours, Is.EqualTo(6));
        Assert.That(r.BudgetCapCoins, Is.EqualTo(123));
        Assert.That(BazaarPlanPreferences.Resolve(new ModSettings()).Strategies, Is.EqualTo(new[] { "Active" }));
        Assert.That(BazaarPlanPreferences.Resolve(new ModSettings { BazaarMode = BazaarPlanMode.Investing }).Strategies, Is.EqualTo(new[] { "Investing" }));
    }

    [Test]
    public void HorizonZeroResolvesTo24OnlyWhenInvesting()
    {
        Assert.That(BazaarPlanPreferences.Resolve(new ModSettings { BazaarMode = BazaarPlanMode.Investing }).InvestingHorizonHours, Is.EqualTo(24));
        Assert.That(BazaarPlanPreferences.Resolve(new ModSettings { BazaarMode = BazaarPlanMode.Both }).InvestingHorizonHours, Is.EqualTo(24));
        Assert.That(BazaarPlanPreferences.Resolve(new ModSettings { BazaarMode = BazaarPlanMode.Active, BazaarInvestingHorizonHours = 12 }).InvestingHorizonHours, Is.Null);
    }
}
