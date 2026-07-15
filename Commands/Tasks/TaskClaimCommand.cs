using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC.Tasks;

[CommandDescription(
    "Claim a task so its estimate uses your data",
    "Usage: /cofl taskclaim <task name>",
    "Claiming tells the tracker which task you are doing,",
    "improving the estimate and the live doer count.",
    "Use /cofl taskclaim none to clear it.")]
public class TaskClaimCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var name = Convert<string>(arguments)?.Trim() ?? string.Empty;
        var playerId = socket.SessionInfo?.McName;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            socket.SendMessage($"{MinecraftSocket.COFLNET}{McColorCodes.RED}Could not determine your account, try again in a moment.");
            return Task.CompletedTask;
        }

        string claimed = null;
        if (!string.IsNullOrWhiteSpace(name) && !name.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var task = socket.GetService<ModCommands.Services.TaskService>().Tasks
                .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? socket.GetService<ModCommands.Services.TaskService>().Tasks
                    .FirstOrDefault(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (task == null)
            {
                socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Task {name} was not found.")
                    .CoflCommand<TaskCommand>("Open the task list", "", "See available tasks"));
                return Task.CompletedTask;
            }
            claimed = task.Name;
        }

        socket.GetService<IStateUpdateService>().Produce(playerId, new()
        {
            Kind = UpdateMessage.UpdateKind.TaskClaim,
            ClaimedTask = claimed,
            ReceivedAt = DateTime.UtcNow,
            PlayerId = playerId,
            UserId = socket.UserId
        });

        if (claimed == null)
            socket.SendMessage($"{MinecraftSocket.COFLNET}{McColorCodes.GREEN}Cleared your claimed task.");
        else
            socket.SendMessage($"{MinecraftSocket.COFLNET}{McColorCodes.GREEN}Claimed {McColorCodes.AQUA}{claimed}{McColorCodes.GREEN}. Estimates will now prefer your data for it.");
        return Task.CompletedTask;
    }
}
