using System.Collections.Generic;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Enable or disable the trade overlay", "Usage: /cofl tradegui <on/off>")]
public class TradeGuiCommand : McCommand
{
    public override bool IsPublic => true;

    public override IEnumerable<KeyValuePair<string, string>> CompletionArguments => new[]
    {
        new KeyValuePair<string, string>("on", "Enable the trade overlay"),
        new KeyValuePair<string, string>("off", "Disable the trade overlay")
    };

    public override Task Execute(MinecraftSocket socket, string arguments) => Execute((IMinecraftSocket)socket, arguments);

    internal Task Execute(IMinecraftSocket socket, string arguments)
    {
        var state = (Convert<string>(arguments) ?? "").Trim().ToLowerInvariant();
        bool? enabled;
        switch (state)
        {
            case "on": enabled = true; break;
            case "off": enabled = false; break;
            case "": enabled = null; break;
            default:
                socket.SendMessage(COFLNET + "Usage: /cofl tradegui <on/off>");
                return Task.CompletedTask;
        }
        socket.Send(Response.Create("tradeGui", new { enabled }));
        return Task.CompletedTask;
    }
}
