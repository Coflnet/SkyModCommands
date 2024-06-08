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
        using var db = socket.CreateActivity("PingMeassured", socket.ConSpan);
        db?.AddTag("ping", ping);
        Console.WriteLine($"Ping of {ping}ms from {socket.SessionInfo.McName} {socket.ClientIp} {socket.SessionInfo.McUuid} {socket.UserId}");
        socket.Dialog(db => db.MsgLine($"Your Ping to execute Coflnet commands is: {McColorCodes.AQUA}{ping}ms")
            .Msg($"{McColorCodes.GRAY}This is the time it takes for your command to reach the server and get executed and sent back to you. Its only partially related to the time your receive flips in."));
    }
}