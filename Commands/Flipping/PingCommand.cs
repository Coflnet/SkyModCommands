using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Checks your ping to the Coflnet server")]
public class PingCommand : McCommand
{
    public override bool IsPublic => true;
    private ConcurrentDictionary<string, List<double>> pings = new();
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
        var thisSession = pings.GetOrAdd(sessionId, (a) => new());
        thisSession.Add(ping);
        if(thisSession.Count < 4)
        {
            await Task.Delay(100);
            socket.ExecuteCommand($"/cofl ping {sessionId} {DateTime.UtcNow.Ticks}");
            return;
        }
        var average = thisSession.Average();
        Console.WriteLine($"Ping of {ping}ms from {socket.SessionInfo.McName} {socket.ClientIp} {socket.SessionInfo.McUuid} {socket.UserId} {average}");
        socket.Dialog(db => db.MsgLine($"Your Ping to execute Coflnet commands is: {McColorCodes.AQUA}{socket.FormatPrice(average)}ms")
            .Msg($"{McColorCodes.GRAY}This is the time it takes for your command to reach the server and get executed and sent back to you. Its only partially related to the time your receive flips in."));
        pings.TryRemove(sessionId, out _);
    }
}