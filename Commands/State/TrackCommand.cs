using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.MC;
public class TrackCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!arguments.Contains("besthotkey"))
            return;
        socket.SessionInfo.BestHotkeyUsageCount++;
        if (socket.SessionInfo.BestHotkeyUsageCount > 5
            && socket.SessionInfo.BestHotkeyUsageCount % 10 == 0
            && socket.sessionLifesycle.CurrentDelay == TimeSpan.Zero
            && !socket.Settings.BlockHighCompetitionFlips)
        {
            Console.WriteLine($"Prompting to block high competition flips {socket.SessionInfo.BestHotkeyUsageCount} {socket.SessionInfo.McUuid} ({socket.SessionInfo.McName})");
            using var context = new HypixelContext();
            var profile = await context.Players.FindAsync(socket.SessionInfo.McUuid);
            var minBidTime = DateTime.UtcNow.AddMinutes(-10);
            var purchseCount = await context.Bids.Where(a => a.BidderId == profile.Id && a.Timestamp > minBidTime).CountAsync();
            if (purchseCount > 0)
            {
                Console.WriteLine("aborting because purchased " + purchseCount);
                return;
            }
            socket.Dialog(db => db.MsgLine($"{McColorCodes.YELLOW}It seems like you are struggling to purchase flips.")
                .MsgLine($"Would you like to only get flips with less competition?\n{McColorCodes.GRAY}(hover for info)", null,
                    "This will hide flips with high competition, making it easier to get flips.\n"
                    + "This is done by analyzing what gets bought quickly possibly by bots.\n"
                    + "Anything that is regulary bought quickly will be hidden.\n"
                    + "You can always all flips again."
                )
                .CoflCommand<SetCommand>($"{McColorCodes.GREEN} [click to enable] ", "blockHighCompetition true", "Hide high competition flips")
                .DialogLink<EchoDialog>("[no thanks]", "Alright, good luck then :)", "Don't enable")
                .AddMargin("-----------------------------------"));
            return;
        }
    }
}
