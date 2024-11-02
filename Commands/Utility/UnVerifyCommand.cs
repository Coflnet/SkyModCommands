using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.McConnect.Api;

namespace Coflnet.Sky.Commands.MC;

public class UnVerifyCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var accountsTask = socket.sessionLifesycle.GetMinecraftAccountUuids();
        var passedName = arguments.Trim('"');
        var uuid = await socket.GetPlayerUuid(passedName);
        var accounts = (await accountsTask).ToHashSet();
        if (!accounts.Contains(uuid))
        {
            socket.Dialog(db => db.MsgLine("This account is/was not verified."));
            return;
        }
        var trackingService = socket.GetService<FlipTrackingService>();
        var delayInfo = await trackingService.GetSpeedComp(accounts);
        if (delayInfo.BadIds.Count > 0)
        {
            socket.Dialog(db => db.MsgLine("You can't unverify accounts as one of them was blacklisted."));
            return;
        }
        var connectApi = socket.GetService<IConnectApi>();
        var user = await connectApi.ConnectMinecraftMcUuidGetAsync(uuid);
        if (user.ExternalId == socket.AccountInfo.UserId)
        {
            socket.Dialog(db => db.MsgLine("The account needs to be most recently used with another gmail."));
            return;
        }
        var premiumService = socket.GetService<PremiumService>();
        var isPremium = await premiumService.ExpiresWhen(user.ExternalId);
        if (isPremium > DateTime.UtcNow + TimeSpan.FromDays(7))
        {
            socket.Dialog(db => db.MsgLine("The gmail account the account was most recently used on doesn't have more than 7 days of premium left. Thats required to proof its not a throwaway account."));
            return;
        }
        await connectApi.ConnectUserUserIdMcUuidDeleteAsync(user.ExternalId, uuid);
        socket.Dialog(db => db.MsgLine("Account unverified, have a nice day."));
    }
}
