using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Checks your ping to the Coflnet server")]
public class PingCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
        var sessionId = socket.SessionInfo.SessionId;
        if (args.Length <= 1)
        {
            socket.ExecuteCommand($"/cofl ping {sessionId} {DateTime.UtcNow.Ticks}");
            socket.Dialog(db => db.MsgLine($"Testing ping"));
            return;
        }
        var returnedSessionId = args[0];
        var time = new DateTime(long.Parse(args[1]));
        if (returnedSessionId != sessionId)
        {
            socket.Dialog(db => db.MsgLine($"This command should be called without any arguments {returnedSessionId} {sessionId}"));
            return;
        }
        var ping = (DateTime.UtcNow - time).TotalMilliseconds;
        if (ping < 10)
        {
            Console.WriteLine($"Ping of {ping}ms from {socket.SessionInfo.McName} {socket.ClientIp} {socket.SessionInfo.McUuid} {socket.UserId}");
        }
        socket.Dialog(db => db.MsgLine($"Your Ping to Coflnet is: {McColorCodes.AQUA}{ping}ms"));
    }
}