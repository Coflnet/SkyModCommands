using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.MC.Tasks;

[CommandDescription("Shows a detailed breakdown for a specific profit task")]
public class TaskDetailsCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var taskName = Convert<string>(arguments)?.Trim() ?? string.Empty;
        if (taskName.StartsWith("/cofl taskdetails ", StringComparison.OrdinalIgnoreCase))
            taskName = taskName.Substring("/cofl taskdetails ".Length).Trim();
        if (string.IsNullOrWhiteSpace(taskName))
        {
            socket.Dialog(db => db.MsgLine("Usage: /cofl taskdetails <task name>")
                .CoflCommand<TaskCommand>("Click here to open the task list", "", "Open tasks"));
            return;
        }

        // Player state is keyed by Minecraft NAME, not uuid - see TaskCommand.GetElements.
        var result = await socket.GetService<TaskService>().GetResult(socket.SessionInfo.McName, taskName);
        if (result == null)
        {
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Task {taskName} was not found.")
                .CoflCommand<TaskCommand>("Back to tasks", "", "Return to the task list"));
            return;
        }
        result = TaskCommand.PrepareTaskResult(result);

        var bazaarTags = await GetBazaarProductIds(socket);
        socket.Dialog(db => BuildDialog(socket, db, result, bazaarTags));
    }

    /// <summary>
    /// Product ids currently sold on the bazaar - used by <see cref="BuildStepByStep"/> to decide
    /// whether a required item's "buy this" click should be a bazaar or auction house search (same
    /// choice PrimaryAction/OnClick already make elsewhere - see HotkeyCommand.IsOnBazaar/NpcCommand).
    /// </summary>
    private static async Task<HashSet<string>> GetBazaarProductIds(MinecraftSocket socket)
    {
        try
        {
            var prices = await socket.GetService<IBazaarApi>().GetAllPricesAsync();
            return prices.Select(p => p.ProductId).ToHashSet();
        }
        catch (Exception e)
        {
            dev.Logger.Instance.Error(e, "loading bazaar prices for taskdetails");
            return new HashSet<string>();
        }
    }

    internal static DialogBuilder BuildDialog(MinecraftSocket socket, DialogBuilder db, TaskResult result, HashSet<string> bazaarTags = null)
    {
        var typeTag = result.Type switch
        {
            TaskType.NUMBER_1 => $"{McColorCodes.DARK_AQUA}Passive",
            TaskType.NUMBER_2 => $"{McColorCodes.GOLD}Limited",
            _ => $"{McColorCodes.GREEN}Active"
        };

        db.MsgLine($"{McColorCodes.YELLOW}{result.Name}")
            .MsgLine($"{McColorCodes.GOLD}{socket.FormatPrice(result.ProfitPerHour)} /h {McColorCodes.GRAY}[{typeTag}{McColorCodes.GRAY}]")
            .MsgLine(result.Message)
            .LineBreak();

        if (!string.IsNullOrWhiteSpace(result.PrimaryAction))
        {
            db.Button(GetPrimaryActionLabel(result.PrimaryAction), result.PrimaryAction, "Run the task's primary action")
                .Msg(" ")
                .CoflCommandButton<TaskCommand>("Back", "", "Return to the task list")
                .LineBreak()
                .LineBreak();
        }
        else
        {
            db.CoflCommandButton<TaskCommand>("Back", "", "Return to the task list")
                .LineBreak()
                .LineBreak();
        }

        BuildStepByStep(db, result, bazaarTags);

        if (!result.IsAccessible)
        {
            db.MsgLine($"{McColorCodes.RED}Currently unavailable")
                .MsgLine(result.InaccessibleReason ?? "This task cannot be done right now.");
            if (result.NextAvailableAt.HasValue)
            {
                db.MsgLine($"{McColorCodes.YELLOW}Next available: {FormatAbsoluteTime(result.NextAvailableAt.Value)} {McColorCodes.GRAY}({FormatRelativeTime(result.NextAvailableAt.Value - DateTime.UtcNow)})");
            }
            db.LineBreak();
        }
        else if (result.NextAvailableAt.HasValue)
        {
            db.MsgLine($"{McColorCodes.YELLOW}Available until / next reset context: {FormatAbsoluteTime(result.NextAvailableAt.Value)} {McColorCodes.GRAY}({FormatRelativeTime(result.NextAvailableAt.Value - DateTime.UtcNow)})")
                .LineBreak();
        }

        if (!string.IsNullOrWhiteSpace(result.Breakdown?.HowTo))
        {
            db.MsgLine($"{McColorCodes.AQUA}How to")
                .MsgLine(result.Breakdown.HowTo)
                .LineBreak();
        }

        if (result.Breakdown?.RequiredItems?.Count > 0)
        {
            db.MsgLine($"{McColorCodes.AQUA}Requirements");
            foreach (var item in result.Breakdown.RequiredItems.Take(8))
            {
                var pricePart = item.EstimatedPrice > 0 ? $" {McColorCodes.DARK_GRAY}({socket.FormatPrice(item.EstimatedPrice)})" : string.Empty;
                var reasonPart = string.IsNullOrWhiteSpace(item.Reason) ? string.Empty : $" {McColorCodes.GRAY}- {item.Reason}";
                db.MsgLine($"{McColorCodes.YELLOW}{item.Name ?? item.ItemTag}{pricePart}{reasonPart}");
            }
            db.LineBreak();
        }

        if (result.Breakdown?.Drops?.Count > 0)
        {
            db.MsgLine($"{McColorCodes.AQUA}Result breakdown");
            foreach (var drop in result.Breakdown.Drops.OrderByDescending(d => d.ContributionPerHour).Take(8))
            {
                db.MsgLine($"{McColorCodes.YELLOW}{drop.Name ?? drop.ItemTag}{McColorCodes.GRAY}: {drop.RatePerHour:F1}/h -> {McColorCodes.GREEN}{socket.FormatPrice((long)drop.ContributionPerHour)}");
            }
            db.LineBreak();
        }

        if (result.Breakdown != null)
        {
            db.MsgLine($"{McColorCodes.AQUA}Timing and source")
                .MsgLine($"{McColorCodes.GRAY}Source: {result.Breakdown.Source ?? "unknown"}")
                .MsgLine($"{McColorCodes.GRAY}Category: {result.Breakdown.Category ?? "Other"}")
                .If(() => result.Breakdown.ActionsPerHour > 0, d => d.MsgLine($"{McColorCodes.GRAY}Expected pace: {result.Breakdown.ActionsPerHour:F1} {result.Breakdown.ActionUnit}/h"))
                .If(() => result.Breakdown.TrackedHours > 0, d => d.MsgLine($"{McColorCodes.GRAY}Tracked time: {result.Breakdown.TrackedHours:F2}h"));
            if (result.Breakdown.Effects?.Count > 0)
            {
                db.LineBreak().MsgLine($"{McColorCodes.AQUA}Helpful effects");
                foreach (var effect in result.Breakdown.Effects.Take(5))
                {
                    db.MsgLine($"{McColorCodes.YELLOW}{effect.Name}{McColorCodes.GRAY}: {effect.Description}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Details))
        {
            db.LineBreak()
                .MsgLine($"{McColorCodes.AQUA}Notes")
                .MsgLine(result.Details);
        }

        return db;
    }

    /// <summary>
    /// Numbered, clickable step-by-step guide (see SkyPlayerState's MethodTask.Steps): each step
    /// that has an OnClick runs it as the button click - a URL opens in the browser, "suggest:"
    /// suggests a command, anything else runs as a command (same semantics as every other OnClick
    /// in this mod - see the Fabric mod's Utils.java). Also surfaces "[Wiki]" and "[Map of &lt;zone&gt;]"
    /// buttons when the task/its zone have a curated/derived wiki page.
    /// </summary>
    /// <param name="db">Dialog to append to.</param>
    /// <param name="result">The task result whose Breakdown.Steps get rendered.</param>
    /// <param name="bazaarTags">
    /// Optional - when given, a step that names a RequiredItem but has no OnClick of its own gets
    /// made clickable to buy that item (bazaar if its tag is in this set, else an auction house
    /// search - the same bazaar-vs-auction-house choice PrimaryAction/OnClick already use elsewhere,
    /// see HotkeyCommand.IsOnBazaar/NpcCommand) and its estimated price is appended to the step text.
    /// </param>
    internal static void BuildStepByStep(DialogBuilder db, TaskResult result, HashSet<string> bazaarTags = null)
    {
        var breakdown = result.Breakdown;
        if (breakdown == null || (breakdown.Steps?.Count ?? 0) == 0)
            return;

        db.MsgLine($"{McColorCodes.AQUA}Step by step");
        foreach (var step in breakdown.Steps)
        {
            var onClick = step.OnClick;
            var hover = "Click to do this step";
            var text = step.Text;
            if (string.IsNullOrWhiteSpace(onClick) && bazaarTags != null)
            {
                var buyableItem = FindNamedRequiredItem(step.Text, breakdown.RequiredItems);
                if (buyableItem != null)
                {
                    onClick = BuildBuyCommand(buyableItem, bazaarTags);
                    hover = $"Click to buy {buyableItem.Name ?? buyableItem.ItemTag}";
                    if (buyableItem.EstimatedPrice > 0)
                        text += $" {McColorCodes.DARK_GRAY}({FormatProvider.FormatPriceShort(buyableItem.EstimatedPrice)})";
                }
            }
            var line = $"{McColorCodes.YELLOW}{step.Number}. {McColorCodes.GRAY}{text}";
            if (!string.IsNullOrWhiteSpace(onClick))
                db.MsgLine(line, onClick, hover);
            else
                db.MsgLine(line);
        }

        var hasWiki = !string.IsNullOrWhiteSpace(breakdown.WikiUrl);
        var hasMap = !string.IsNullOrWhiteSpace(breakdown.WhereWikiUrl) && breakdown.WhereWikiUrl != breakdown.WikiUrl;
        if (hasWiki || hasMap)
        {
            if (hasWiki)
                db.Button("[Wiki]", breakdown.WikiUrl, "Opens the wiki page for this method").Msg(" ");
            if (hasMap)
            {
                var zoneLabel = breakdown.Where ?? breakdown.Island ?? "zone";
                db.Button($"[Map of {zoneLabel}]", breakdown.WhereWikiUrl, "Opens the wiki page with the island map");
            }
            db.LineBreak();
        }
        db.LineBreak();
    }

    /// <summary>
    /// Finds the RequiredItem (if any) whose name or item tag is mentioned in a step's text - used
    /// to make a plain "Get X first." step clickable without its own hand-written OnClick.
    /// </summary>
    internal static RequiredItem FindNamedRequiredItem(string stepText, List<RequiredItem> requiredItems)
    {
        if (string.IsNullOrWhiteSpace(stepText) || requiredItems == null)
            return null;
        return requiredItems.FirstOrDefault(r =>
            (!string.IsNullOrWhiteSpace(r.Name) && stepText.Contains(r.Name, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(r.ItemTag) && stepText.Contains(r.ItemTag, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Same bazaar-vs-auction-house command choice used elsewhere for PrimaryAction/OnClick (see
    /// HotkeyCommand.IsOnBazaar, NpcCommand): a bazaar search when the item is a bazaar product,
    /// else an auction house search. BazaarUtils.GetSearchValue turns shard/enchantment tags into
    /// the right display name for the search box.
    /// </summary>
    internal static string BuildBuyCommand(RequiredItem item, HashSet<string> bazaarTags)
    {
        var isBazaar = bazaarTags?.Contains(item.ItemTag) == true;
        var searchValue = BazaarUtils.GetSearchValue(item.ItemTag, item.Name);
        return isBazaar ? $"/bz {searchValue}" : $"/ahs {searchValue}";
    }

    internal static string GetPrimaryActionLabel(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "Action";
        if (action.StartsWith("/warp ", StringComparison.OrdinalIgnoreCase))
            return "Warp";
        if (action.StartsWith("/bz ", StringComparison.OrdinalIgnoreCase))
            return "Bazaar";
        if (action.StartsWith("/viewauction ", StringComparison.OrdinalIgnoreCase))
            return "Auction";
        return "Action";
    }

    internal static string FormatAbsoluteTime(DateTime dateTime)
    {
        return dateTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
    }

    internal static string FormatRelativeTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "now";
        if (remaining.TotalMinutes < 1)
            return "in under 1m";
        if (remaining.TotalHours < 1)
            return $"in {(int)Math.Ceiling(remaining.TotalMinutes)}m";
        if (remaining.TotalDays < 1)
            return $"in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"in {(int)remaining.TotalDays}d {remaining.Hours}h";
    }
}
