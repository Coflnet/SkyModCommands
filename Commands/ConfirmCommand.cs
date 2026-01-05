using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class ConfirmCommand : McCommand
{
    public override Task Execute(MinecraftSocket socket, string arguments)
    {
        var cmd = Convert<string>(arguments);
        if (string.IsNullOrEmpty(cmd))
        {
            socket.Dialog(db => db.MsgLine("Usage: /cofl confirm <command to execute>", null, "Provide a command to confirm"));
            return Task.CompletedTask;
        }

        // If the provided command does not start with a slash, assume it's a /cofl subcommand and prefix it.
        var exec = cmd.StartsWith("/") ? cmd : $"/cofl {cmd}";

        // Show a dialog asking to confirm executing the command.
        socket.Dialog(db => db.MsgLine($"Do you want to execute: {McColorCodes.AQUA}{exec}")
            .Button("Confirm", exec, "Execute the command")
            .Button("Cancel", null, "Cancel"));

        return Task.CompletedTask;
    }
}
