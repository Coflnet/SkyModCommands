using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

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

        var tasks = TaskCatalog.Create();
        var task = tasks.Values.FirstOrDefault(t => t.Name.Equals(taskName, StringComparison.OrdinalIgnoreCase))
               ?? tasks.Values.FirstOrDefault(t => t.Name.Contains(taskName, StringComparison.OrdinalIgnoreCase));
        if (task == null)
        {
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Task {taskName} was not found.")
                .CoflCommand<TaskCommand>("Back to tasks", "", "Return to the task list"));
            return;
        }

        var parameters = await TaskCommand.BuildParameters(socket, TaskCommand.CreateCalculationCache());
        var result = TaskCommand.PrepareTaskResult(await task.Execute(parameters));

        socket.Dialog(db => BuildDialog(socket, db, result));
    }

    internal static DialogBuilder BuildDialog(MinecraftSocket socket, DialogBuilder db, TaskResult result)
    {
        var typeTag = result.Type switch
        {
            TaskType.Passive => $"{McColorCodes.DARK_AQUA}Passive",
            TaskType.Limited => $"{McColorCodes.GOLD}Limited",
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