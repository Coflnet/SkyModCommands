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
        var startTime = DateTime.UtcNow;
        var args = JsonConvert.DeserializeObject<string>(arguments).Split(' ');
        var sessionId = socket.SessionInfo.SessionId;
        if (args.Length <= 1)
        {
            if (MinecraftSocket.NextFlipTime > DateTime.UtcNow.AddSeconds(52) && MinecraftSocket.NextFlipTime < DateTime.UtcNow.AddSeconds(65))
                socket.Dialog(db => db.MsgLine($"The ah updates soon. Ping may appear higher now than it actually is because cpu is used to load auctions as fast as possible for you."));
            socket.Dialog(db => db.MsgLine($"Testing ping"));
            socket.ExecuteCommand($"/cofl ping {sessionId} {DateTime.UtcNow.Ticks}");
            return;
        }
        var returnedSessionId = args[0];
        var time = new DateTime(long.Parse(args[1]));
        if (returnedSessionId != sessionId)
        {
            socket.Dialog(db => db.MsgLine($"This command should be called without any arguments {returnedSessionId} {sessionId}"));
            return;
        }
        var ping = (startTime - time).TotalMilliseconds;
        using var db = socket.CreateActivity("PingMeassured", socket.ConSpan);
        db?.AddTag("ping", ping);
        var thisSession = pings.GetOrAdd(sessionId, (a) => new());
        thisSession.Add(ping);
        if (thisSession.Count < 4)
        {
            Console.WriteLine($"Part ping {ping}");
            await Task.Delay(100);
            socket.ExecuteCommand($"/cofl ping {sessionId} {DateTime.UtcNow.Ticks}");
            return;
        }
        var average = thisSession.Average();
        var lowest = thisSession.Min();
        db?.AddTag("minping", lowest);
        if(average > 120 && socket.CurrentRegion == "eu")
            socket.Dialog(db => db.CoflCommand<SwitchRegionCommand>("Your ping is quite high, you might want to consider switching to the US region.", "us", "Click to switch to us servers"));
        Console.Write($"Ping of {ping}ms from {socket.SessionInfo.McName} {socket.ClientIp} {socket.SessionInfo.McUuid} {average}");
        Console.WriteLine($" {socket.sessionLifesycle?.UserId?.Value}");
        socket.Dialog(db => db.MsgLine($"Your Ping to execute SkyCofl commands is: {McColorCodes.AQUA}{socket.FormatPrice(average)}ms")
            .Msg($"The time to receive flips is estimated to be {McColorCodes.AQUA}{socket.FormatPrice(lowest / 2)}ms", 
                null, "That is half of your lowest ping\nFlips only need to travel to you but not back"));
        pings.TryRemove(sessionId, out _);
    }
}