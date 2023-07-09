using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.MC;
public class CompensateCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (!socket.GetService<ModeratorService>().IsModerator(socket))
            throw new CoflnetException("forbidden", "You need to be a moderator to use this command");
        if (!int.TryParse(socket.UserId, out var userId) || userId > 7)
            throw new CoflnetException("forbidden", "You are not allowed to use this command");
        var args = arguments.Trim('"').Split(' ');
        var userName = args[0];
        var amount = int.Parse(args[1]);
        var reference = string.Join(' ', args[2..]);
        var topUpApi = socket.GetService<TopUpApi>();
        var accountService = socket.GetService<McAccountService>();

        var uuid = await socket.GetPlayerUuid(userName);
        if (uuid == null)
            throw new CoflnetException("invalid_player", "The player you specified is invalid");
        var user = await accountService.GetUserId(uuid);
        if (user == null)
            throw new CoflnetException("invalid_player", "No Coflnet Account found for player");
        var info = await topUpApi.TopUpCustomPostAsync(user.ExternalId, new()
        {
            Amount = amount,
            ProductId = "compensation",
            Reference = reference
        });
        socket.Dialog(db => db.Msg($"Compensation sent to {userName}", null, "close"));
        socket.GetService<ILogger<CompensateCommand>>()
            .LogInformation($"Compensation sent to {userName} for {amount} with reference {reference} by {socket.UserId} ({socket.SessionInfo.McUuid})");

    }
}