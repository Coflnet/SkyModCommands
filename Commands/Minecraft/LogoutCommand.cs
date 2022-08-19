using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class LogoutCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var account = socket.sessionLifesycle?.AccountInfo?.Value;
            if (account == null)
                return;
            var list = account.ConIds;
            foreach (var item in list)
            {
                System.Console.WriteLine("logging out " + item);
                await socket.GetService<SettingsService>().UpdateSetting<string>("mod", item, "logedOut");
            }
            var userId = account.UserId;
            account.ConIds.Clear();
            // trigger logout
            account.ConIds.Add("logout");
            await socket.GetService<SettingsService>().UpdateSetting(userId.ToString(), "accountInfo", account);
            // reset
            account.ConIds.Clear();
            await socket.GetService<SettingsService>().UpdateSetting(userId.ToString(), "accountInfo", account);

        }
    }
}