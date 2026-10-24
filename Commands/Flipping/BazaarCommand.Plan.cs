using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Commands.MC;

/// <summary>Resolved arguments of a personal bazaar plan (settings defaults with command overrides applied).</summary>
public record BazaarPlanArgs(BazaarPlanMode Mode, int Hours, long Budget);

/// <summary>
/// Personal bazaar plan: <c>/cofl bz [active|investing|both] [hours] [budget]</c> and <c>/cofl bz track</c>.
/// This command only ever recommends and tracks; it never places an order.
/// </summary>
public partial class BazaarCommand
{
    private static readonly TimeSpan PlanThrottle = TimeSpan.FromSeconds(10);
    private const string Usage = "§cUsage: /cofl bz [active|investing|both] [hours 1-48] [budget]  ·  /cofl bz track <quoteId> <itemTag>";

    public override async Task Execute(MinecraftSocket socket, string args)
    {
        var input = (Convert<string>(args) ?? "").Trim();
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!IsPlanRequest(parts))
        {
            await base.Execute(socket, args);
            return;
        }
        var userId = TryGetUserId(socket);
        if (userId == null)
        {
            await base.Execute(socket, args); // not logged in: legacy list as before
            return;
        }
        if (parts.Length > 0 && parts[0].Equals("track", StringComparison.OrdinalIgnoreCase))
        {
            await Track(socket, userId, parts);
            return;
        }
        if (!TryParsePlanArgs(socket, parts, out var planArgs))
            return;
        await ShowPlan(socket, userId, planArgs, bypassThrottle: false);
    }

    private static bool IsPlanRequest(string[] parts)
    {
        if (parts.Length == 0)
            return true;
        var first = parts[0].ToLowerInvariant();
        if (first is "active" or "investing" or "both" or "track")
            return true;
        if (first is "h" or "history" or "l" or "list" || double.TryParse(first, out _))
            return false;
        // "<word> <number>" can only be a mistyped mode (a legacy search never takes a number after it)
        return parts.Length >= 2 && double.TryParse(parts[1], out _);
    }

    private static string TryGetUserId(MinecraftSocket socket)
    {
        try { return socket.UserId; }
        catch (NoLoginException) { return null; }
    }

    private static bool IsStarterPremium(MinecraftSocket socket)
        => socket.sessionLifesycle?.TierManager?.HasAtLeast(AccountTier.STARTER_PREMIUM) ?? false;

    private static string ModeName(BazaarPlanMode mode) => mode.ToString().ToLowerInvariant();

    private bool TryParsePlanArgs(MinecraftSocket socket, string[] parts, out BazaarPlanArgs resolved)
    {
        resolved = null;
        var settings = socket.Settings?.ModSettings ?? new ModSettings();
        var mode = settings.BazaarMode;
        var hours = settings.BazaarInvestingHorizonHours == 0 ? BazaarPlanPreferences.DefaultHorizonHours : settings.BazaarInvestingHorizonHours;
        var budget = settings.BazaarPlanBudget;
        var rest = parts;
        if (parts.Length > 0)
        {
            if (!Enum.TryParse<BazaarPlanMode>(parts[0], true, out var parsedMode) || !Enum.IsDefined(parsedMode))
            {
                socket.Dialog(db => db.MsgLine(Usage));
                return false;
            }
            mode = parsedMode;
            rest = parts.Skip(1).ToArray();
        }
        if (rest.Length > 0)
        {
            var token = rest[0].TrimEnd('h', 'H');
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || h < 1 || h > 48 || h != Math.Floor(h))
            {
                var retry = $"/cofl bz {ModeName(mode)}";
                socket.Dialog(db => db.MsgLine($"§cHours must be a whole number between 1 and 48, got '{rest[0]}'. §7[click to retry with the default]", retry,
                    $"Runs {retry}\nThe horizon is never silently changed"));
                return false;
            }
            hours = (int)h;
        }
        if (rest.Length > 1)
        {
            if (!NumberParser.TryLong(rest[1], out budget) || budget < 0)
            {
                socket.Dialog(db => db.MsgLine($"§cCould not read the budget '{rest[1]}', use a number like 5m or 250k", $"/cofl bz {ModeName(mode)} {hours}"));
                return false;
            }
        }
        resolved = new BazaarPlanArgs(mode, hours, budget);
        return true;
    }

    private static string Command(BazaarPlanMode mode, int hours, long budget)
        => $"/cofl bz {ModeName(mode)}" + (mode == BazaarPlanMode.Active && budget == 0 ? "" : $" {hours}") + (budget > 0 ? $" {budget}" : "");

    // ---------------------------------------------------------------- loading / saving goals (overridable for tests)

    protected virtual async Task<GoalList> LoadGoals(MinecraftSocket socket, string userId)
        => await socket.GetService<SettingsService>().GetCurrentValue(userId, GoalCommand.SettingsKey, () => new GoalList());

    protected virtual Task SaveGoals(MinecraftSocket socket, string userId, GoalList list)
        => socket.GetService<SettingsService>().UpdateSetting(userId, GoalCommand.SettingsKey, list);

    protected virtual async Task<Dictionary<string, string>> GetItemNames(MinecraftSocket socket)
    {
        try
        {
            var names = await socket.GetService<IItemsApi>().ItemNamesGetAsync();
            return names?.GroupBy(n => n.Tag).ToDictionary(g => g.Key, g => g.First().Name) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    // ---------------------------------------------------------------- plan

    /// <summary>
    /// Asks the planner for this user (preview only, nothing is reserved) and renders the result.
    /// Returns the allocation shown, or null when throttled, impossible or when the planner failed.
    /// </summary>
    private async Task<TraderAllocationDto> ShowPlan(MinecraftSocket socket, string userId, BazaarPlanArgs args, bool bypassThrottle, string preamble = null)
    {
        var info = socket.SessionInfo;
        var now = DateTime.UtcNow;
        if (!bypassThrottle && now - info.LastBazaarPlanAt < PlanThrottle)
        {
            socket.Dialog(db => db.MsgLine("§7Please wait a few seconds before asking for a new plan"));
            return null;
        }

        var strategies = args.Mode switch
        {
            BazaarPlanMode.Investing => new[] { "Investing" },
            BazaarPlanMode.Both => new[] { "Active", "Investing" },
            _ => new[] { "Active" }
        };
        var purse = info.Purse;
        if (purse <= 0 && args.Budget > 0)
            purse = args.Budget; // purse unknown but the user named a budget
        if (purse <= 0)
        {
            socket.Dialog(db => db.MsgLine("§cYour purse is not known yet, name a budget: /cofl bz <mode> <hours> <budget>"));
            return null;
        }
        var goals = await LoadGoals(socket, userId) ?? new GoalList();
        var state = new BazaarTraderState(info.McUuid ?? userId, info.SessionTier, info.IsMacroBot, purse,
            BazaarOrderStateHelper.MaxTotalOrders - info.ActiveBazaarOrderCount, strategies,
            strategies.Contains("Investing") ? args.Hours : null, args.Budget,
            info.BazaarOrders, info.SentBazaarOrders,
            (info.Inventory ?? new()).Where(i => !string.IsNullOrEmpty(i?.Tag)).Select(i => i.Tag).Distinct().ToList(),
            BazaarPlanRequestBuilder.ActivePlanGoalTags(goals), info.BazaarOrdersUpdatedAt, info.BazaarOrdersRevision.ToString());
        var trader = BazaarPlanRequestBuilder.Build(state);
        if (trader == null)
        {
            socket.Dialog(db => db.MsgLine("§7No free bazaar order slot or no budget left (open and pending buy orders count against it). Adjust with /cofl bz <mode> <hours> <budget>"));
            return null;
        }

        info.LastBazaarPlanAt = now;
        info.LastBazaarPlanArgs = args;
        FleetAllocateResponse response;
        try
        {
            IConfiguration configuration = null;
            try { configuration = socket.GetService<IConfiguration>(); } catch (Exception) { }
            var request = BazaarPlanRequestBuilder.BuildRequest([trader], 1.0 / BazaarFlipService.InstanceCountFrom(configuration),
                "preview:" + Environment.MachineName, preview: true, includeUnqualifiedInvesting: true);
            response = await socket.GetService<IFleetApi>().FleetAllocatePostAsync(request);
            if (response == null)
                throw new InvalidOperationException("empty planner response");
        }
        catch (Exception e)
        {
            socket.SessionInfo.LastBazaarPlan = null;
            Console.WriteLine($"bazaar planner failed {e.GetType().Name}: {e.Message}");
            if (args.Mode == BazaarPlanMode.Investing)
            {
                // an investing-only request has no meaningful legacy equivalent: the spread list is Active-only quick flips
                var retry = Command(args.Mode, args.Hours, args.Budget);
                socket.Dialog(db => db.MsgLine("§cInvesting plan unavailable right now (planner error); inputs kept. §e[retry]", retry, "Click to ask the planner again"));
                return null;
            }
            socket.Dialog(db => db.MsgLine("§cplanner unavailable, showing market spread"));
            await base.Execute(socket, "\"\"");
            return null;
        }

        info.LastBazaarPlan = response;
        var allocation = response.Assignments?.FirstOrDefault(a => a?.TraderId == trader.Id) ?? response.Assignments?.FirstOrDefault()
            ?? new TraderAllocationDto { TraderId = trader.Id, Orders = new(), InvestingStatus = "NotRequested" };
        var names = await GetItemNames(socket);
        Render(socket, userId, args, response.Quote, allocation, names, preamble);
        return allocation;
    }

    /// <summary>The orders this user may see and act on: everything for paid tiers, the stable daily sample for free users.</summary>
    private static List<OrderRecommendationDto> VisibleOrders(MinecraftSocket socket, string userId, List<OrderRecommendationDto> orders)
    {
        orders ??= new();
        if (IsStarterPremium(socket))
            return orders;
        return FreeTierPlanSampler.SampleIndices(orders.Count, userId, DateTime.UtcNow).Select(i => orders[i]).ToList();
    }

    private void Render(MinecraftSocket socket, string userId, BazaarPlanArgs args, FleetQuoteDto quote, TraderAllocationDto allocation,
        Dictionary<string, string> names, string preamble)
    {
        var now = DateTime.UtcNow;
        var free = !IsStarterPremium(socket);
        var allOrders = allocation.Orders ?? new();
        var visible = VisibleOrders(socket, userId, allOrders);
        var includesInvesting = args.Mode != BazaarPlanMode.Active;
        string Name(string tag) => names.TryGetValue(tag ?? "", out var n) ? n : tag;

        socket.Dialog(db =>
        {
            if (preamble != null)
                db.MsgLine(preamble);
            RenderHeader(db, args);
            if (quote != null)
            {
                if (quote.ExpiresAt <= now)
                    db.MsgLine("§cthis quote has expired, run the command again for fresh prices");
                else
                    db.MsgLine($"§7quote generated {(int)Math.Max(0, (now - quote.GeneratedAt).TotalSeconds)}s ago, valid for {(int)(quote.ExpiresAt - now).TotalSeconds}s");
            }
            if (allocation.InvestingStatus == "Unavailable")
                db.MsgLine($"§cInvesting unavailable: {Reasons(allocation.InvestingReasonCodes)}");
            else if (allocation.InvestingStatus == "StaleAccountState")
                db.MsgLine("§eYour order state is stale, open your bazaar orders overview and ask again");
            else if (allocation.InvestingStatus == "EmptyQualified" && allOrders.Count == 0)
                db.MsgLine($"§7No qualified {ModeName(args.Mode)} opportunities within {args.Hours}h — {Reasons(allocation.InvestingReasonCodes)}");
            else if (allOrders.Count == 0)
                db.MsgLine("§7No recommendations right now");

            foreach (var order in visible)
                RenderOrder(socket, db, order, quote, Name(order.ItemTag));

            var hidden = allOrders.Count - visible.Count;
            var unqualified = allocation.UnqualifiedInvestingCandidates ?? new();
            if (includesInvesting && unqualified.Count > 0 && !free)
            {
                db.MsgLine("§7not qualified (no probability claim)");
                foreach (var candidate in unqualified)
                    RenderCandidate(socket, db, candidate, Name(candidate.ItemTag));
            }
            else if (free)
                hidden += unqualified.Count;
            if (free && hidden > 0)
                db.MsgLine($"§7{hidden} more recommendations with starter premium"); // no hover, no click on purpose
            db.MsgLine("§7Check out your profit with /cl bz history", "/cofl bz history", "Click to view your bazaar profit history");
            return db;
        });
    }

    private static string Reasons(List<string> codes) => codes is { Count: > 0 } ? string.Join(", ", codes) : "no reason given";

    private void RenderHeader(DialogBuilder db, BazaarPlanArgs args)
    {
        db.Msg("§6Bazaar plan ");
        foreach (var mode in new[] { BazaarPlanMode.Active, BazaarPlanMode.Investing, BazaarPlanMode.Both })
        {
            var current = mode == args.Mode;
            db.Button(mode.ToString(), Command(mode, args.Hours, args.Budget), $"Plan {ModeName(mode)} flips", current ? McColorCodes.GREEN : McColorCodes.GOLD);
            db.Msg(" ");
        }
        if (args.Mode != BazaarPlanMode.Active)
            foreach (var hours in new[] { 12, 24, 48 })
            {
                db.Button($"{hours}h", Command(args.Mode, hours, args.Budget), $"Hold up to {hours} hours", hours == args.Hours ? McColorCodes.GREEN : McColorCodes.GOLD);
                db.Msg(" ");
            }
        var budgetCommand = Command(args.Mode, args.Hours, 0);
        db.Button(args.Budget > 0 ? $"budget {args.Budget:N0}" : "budget", budgetCommand,
            "Maximum coins committed across all open and planned buys\nSet one with /cofl bz <mode> <hours> <budget>\n" + (args.Budget > 0 ? "Click to reset to two thirds of your purse" : "Default is two thirds of your purse"));
        db.LineBreak();
    }

    private static string Pct(double rate) => (rate * 100).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Hours(double hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Coins(MinecraftSocket socket, double value)
        => Math.Abs(value) < 1000 ? value.ToString("0.#", CultureInfo.InvariantCulture) : socket.FormatPrice(Math.Round(value));

    private static string Economics(MinecraftSocket socket, OrderRecommendationDto o, string name)
    {
        var prices = o.ExitPrice is { } exit ? $"{Coins(socket, o.EntryPrice)} → {Coins(socket, exit)}" : Coins(socket, o.EntryPrice);
        return $"§e{o.OriginalQuantity}x {name} §7[{o.Mode}] §a{prices} §7net §a+{Coins(socket, o.EstimatedNetProfit)} §7(tax {Pct(o.SellTaxRate)}% {o.TaxSource}) · lock ≤{Hours(o.CapitalLockHorizonHours)}h";
    }

    private void RenderOrder(MinecraftSocket socket, DialogBuilder db, OrderRecommendationDto o, FleetQuoteDto quote, string name)
    {
        var hover = $"The {Hours(o.CapitalLockHorizonHours)}h clock starts when your BUY is placed";
        var label = ConfidenceLabel(o.Confidence);
        if (label != null)
            hover += "\n" + label;
        if (o.Confidence?.ReasonCodes is { Count: > 0 })
            hover += "\nreasons: " + string.Join(", ", o.Confidence.ReasonCodes);
        hover += $"\nquote {quote?.QuoteId}\n§eClick to track this plan";
        db.MsgLine(Economics(socket, o, name), $"/cofl bz track {quote?.QuoteId} {o.ItemTag}", hover);
    }

    private void RenderCandidate(MinecraftSocket socket, DialogBuilder db, OrderRecommendationDto o, string name)
    {
        var hover = "Advisory only: not drafted and no probability is claimed";
        var label = ConfidenceLabel(o.Confidence);
        if (label != null)
            hover += "\n" + label;
        if (o.Confidence?.ReasonCodes is { Count: > 0 })
            hover += "\nreasons: " + string.Join(", ", o.Confidence.ReasonCodes);
        db.MsgLine("§7" + Economics(socket, o, name).Replace("§e", "§7").Replace("§a", "§7"), null, hover);
    }

    /// <summary>Honest description of the fill confidence; null when there is nothing to say. Never turns a missing number into a percent.</summary>
    internal static string ConfidenceLabel(FillConfidenceDto c)
    {
        if (c == null)
            return null;
        string P(double? v) => v is { } x ? (x).ToString("0.0%", CultureInfo.InvariantCulture) : "unknown";
        return c.Status switch
        {
            "Qualified" => $"§a>95% full buy+sell cycle (p {P(c.Probability)}, lower bound {P(c.LowerBound)})",
            "InsufficientEvidence" or "Unavailable" => "§7confidence unknown: insufficient evidence",
            "SimulationOnly" => "§7simulation-only estimate",
            "StaleInputs" => "§estale inputs",
            "NotApplicable" => null,
            "UnsupportedDomain" => "§7no probability claim: unsupported market",
            "NotQualified" => "§7not qualified: no probability claim",
            _ => "§7no probability claim"
        };
    }

    // ---------------------------------------------------------------- track

    private async Task Track(MinecraftSocket socket, string userId, string[] parts)
    {
        if (parts.Length != 3)
        {
            socket.Dialog(db => db.MsgLine("§cUsage: /cofl bz track <quoteId> <itemTag>"));
            return;
        }
        var quoteId = parts[1];
        var tag = parts[2].ToUpperInvariant();
        var info = socket.SessionInfo;
        var plan = info.LastBazaarPlan;
        var now = DateTime.UtcNow;
        var order = plan?.Quote != null && plan.Quote.ExpiresAt > now && plan.Quote.QuoteId == quoteId
            ? VisibleOrders(socket, userId, plan.Assignments?.FirstOrDefault()?.Orders)
                .FirstOrDefault(o => string.Equals(o.ItemTag, tag, StringComparison.OrdinalIgnoreCase))
            : null;
        if (plan?.Quote == null || plan.Quote.ExpiresAt <= now)
        {
            // never track off a stale or missing quote: plan again and let the user confirm what is recommended now
            var args = info.LastBazaarPlanArgs;
            if (args == null)
            {
                var prefs = socket.Settings?.ModSettings ?? new ModSettings();
                args = new BazaarPlanArgs(prefs.BazaarMode, prefs.BazaarInvestingHorizonHours == 0 ? BazaarPlanPreferences.DefaultHorizonHours : prefs.BazaarInvestingHorizonHours, prefs.BazaarPlanBudget);
            }
            var fresh = await ShowPlan(socket, userId, args, bypassThrottle: true, preamble: "§eThat quote has expired and was refreshed. Click a recommendation below to track it.");
            if (fresh != null && !VisibleOrders(socket, userId, fresh.Orders).Any(o => string.Equals(o.ItemTag, tag, StringComparison.OrdinalIgnoreCase)))
                socket.Dialog(db => db.MsgLine($"§c{tag} is no longer recommended"));
            return;
        }
        if (order == null)
        {
            socket.Dialog(db => db.MsgLine("§cThat recommendation is not part of your latest plan, run /cofl bz again"));
            return;
        }

        ChatPartDto first = null;
        await info.BazaarDisplayLock.WaitAsync();
        try
        {
            var list = await LoadGoals(socket, userId) ?? new GoalList();
            list.Goals ??= new();
            if (GoalCommand.HasActiveBazaarFlip(list, tag))
            {
                socket.Dialog(db => db.MsgLine("§cYou already have an active Bazaar flip for this item. Focus or remove it first."));
                return;
            }
            var names = await GetItemNames(socket);
            var name = names.GetValueOrDefault(tag, tag);
            var created = new DateTime(now.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
            var quantity = order.OriginalQuantity;
            var goal = new Goal
            {
                Title = $"Bazaar flip: {name} ×{quantity}",
                RootItemTag = tag,
                Quantity = quantity,
                CreatedAt = created,
                Source = "mod",
                BazaarFlip = new(),
                Root = new GoalStep { Kind = StepKind.Sell, ItemTag = tag, Quantity = quantity },
                Plan = new BazaarPlanInfo
                {
                    Mode = order.Mode,
                    QuoteId = plan.Quote.QuoteId,
                    MarketFrameId = plan.Quote.MarketFrameId,
                    PolicyId = plan.Quote.PolicyId,
                    PolicyVersion = plan.Quote.PolicyVersion,
                    ModelVersion = plan.Quote.ModelVersion,
                    GeneratedAt = plan.Quote.GeneratedAt,
                    ExpiresAt = plan.Quote.ExpiresAt,
                    OriginalQuantity = quantity,
                    EntryPrice = order.EntryPrice,
                    ExitPrice = order.ExitPrice ?? 0,
                    SellTaxRate = order.SellTaxRate,
                    TaxSource = order.TaxSource,
                    EstimatedNetProfit = order.EstimatedNetProfit,
                    HorizonHours = order.CapitalLockHorizonHours,
                    Deadline = null,
                    ConfidenceStatus = order.Confidence?.Status,
                    Probability = order.Confidence?.Probability,
                    LowerBound = order.Confidence?.LowerBound,
                    ReasonCodes = order.Confidence?.ReasonCodes,
                }
            };
            list.Goals.Add(goal);
            list.FocusId = goal.Id;
            await SaveGoals(socket, userId, list);
            socket.Dialog(db => db.MsgLine($"§aTracking {goal.Title}. Nothing is ordered automatically, follow the steps below."));
            first = BazaarFlipProgress.Line(goal, false, name, BazaarUtils.GetSearchValue(tag, name), now);
        }
        finally
        {
            info.BazaarDisplayLock.Release();
        }
        if (first == null)
            return;
        socket.Dialog(db => db.MsgLine(first.Text, first.OnClick, first.Hover));
        if (!string.IsNullOrEmpty(first.OnClick))
            socket.Send(Response.Create("expectCommand", new { command = first.OnClick, ttl = 120 }));
    }
}
