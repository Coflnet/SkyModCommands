using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Requests to be notified about bazaar flips
/// </summary>
public class GetBazaarFlipsCommand : ArgumentsCommand
{
    protected override string Usage => "";

    protected override Task Execute(IMinecraftSocket socket, Arguments args)
    {
        if (!socket.Settings.AllowedFinders.HasFlag(LowPricedAuction.FinderType.Bazaar))
        {
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Your settings currently do not allow bazaar flips, please enable the finder to receive bazaar flip recommendations",
                "/cofl set finders Bazaar," + socket.Settings.AllowedFinders.ToString(), "Click to enable"));
            return Task.CompletedTask;
        }
        socket.Dialog(db => db.MsgLine(
            $"{McColorCodes.GREEN}The bazaar finder is enabled. " +
            $"{McColorCodes.GRAY}Bazaar flips are now distributed automatically — calling this command is no longer required."));
        return Task.CompletedTask;
    }
}