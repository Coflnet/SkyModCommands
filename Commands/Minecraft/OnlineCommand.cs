using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Payments.Client.Api;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System;

namespace Coflnet.Sky.Commands.MC
{
    public class OnlineCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendMessage(COFLNET + $"There are {McColorCodes.AQUA}{FlipperService.Instance.PremiumUserCount}{McColorCodes.GRAY} users connected to this server",
                    null, McColorCodes.GRAY + "there is more than one server");
            var countTask = socket.GetService<Commands.FlipTrackingService>().ActiveFlipperCount();
            var preApiUsers = await socket.GetService<ProductsApi>().ProductsServiceServiceSlugOwnedGetAsync(socket.GetService<IConfiguration>()["PRODUCTS:PRE_API"], start: System.DateTime.UtcNow.AddHours(-1));
            string extra = McColorCodes.ITALIC + " click to buy";
            if(await socket.UserAccountTier() == AccountTier.SUPER_PREMIUM)
                extra = McColorCodes.GREEN + " (you are one of them)";
            var drop = preApiUsers.OrderBy(u=>u.End).Select(u=>u.End).FirstOrDefault(DateTime.UtcNow) - DateTime.UtcNow;
            socket.Dialog(db=>db.CoflCommand<PurchaseCommand>($"{McColorCodes.AQUA}{preApiUsers.Count}{McColorCodes.GRAY} users are using {McColorCodes.RED}pre api{McColorCodes.GRAY}" + extra,
                    "pre_api", $"they are counted in the total, {McColorCodes.AQUA}click to buy an hour\n{McColorCodes.GRAY}The next hour ends in {drop.Minutes} minutes"));
            socket.SendMessage(COFLNET + $"{McColorCodes.AQUA}{await countTask}{McColorCodes.GRAY} players clicked on a flip in the last 3 minutes.",
                    null, McColorCodes.GRAY + "across all plans (free included)");
        }
    }
}