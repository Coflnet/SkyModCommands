using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

public class ForgetUserCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var isModerator = socket.GetService<ModeratorService>().IsModerator(socket);
        if (!isModerator || socket.SessionInfo.McName != "Ekwav")
            throw new CoflnetException("forbidden", "Whoops, you don't seem to be authorized to delete user info");

        var parts = Convert<string>(arguments).Split(' ');
        var id = await IndexerClient.DeleteUser(parts[0], parts[1]);
        socket.Dialog(db => db.MsgLine("Deleted user email").AsGray());
        var settingsService = socket.GetService<SettingsService>();
        await settingsService.UpdateSetting<FlipSettings>(id, "flipSettings", null);
        await settingsService.UpdateSetting<AccountInfo>(id, "accountInfo", null);
        await settingsService.UpdateSetting<AccountSettings>(id, "accountSettings", null);
        socket.Dialog(db => db.MsgLine("Deleted user settings").AsGray());
        var mcConnect = socket.GetService<McConnect.Api.IConnectApi>();
        await mcConnect.ConnectUserUserIdDeleteAsync(id);
        socket.Dialog(db => db.MsgLine("Deleted connected account(s)").AsGray());
    }
}