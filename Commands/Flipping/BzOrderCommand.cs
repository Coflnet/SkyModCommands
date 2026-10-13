using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Fallback for mods older than 2.0.0 which forward unknown /cofl subcommands here.
/// They cannot pre-arm the bazaar sign so this just opens the normal /bz search.
/// </summary>
public class BzOrderCommand : McCommand
{
    private static readonly Regex Pattern = new(@"^\s*(?:search\s+)?\d+\s+(\S.*?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var match = Pattern.Match(Convert<string>(arguments) ?? "");
        if (!match.Success)
        {
            socket.Dialog(db => db.MsgLine("Usage: /cofl bzorder [search] <amount> <item>"));
            return;
        }
        socket.ExecuteCommand("/bz " + match.Groups[1].Value);
        await Task.CompletedTask;
    }
}
