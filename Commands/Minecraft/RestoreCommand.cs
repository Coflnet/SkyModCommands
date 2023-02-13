using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;

namespace Coflnet.Sky.Commands.MC
{
    public class RestoreCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var settings = socket.GetService<SettingsService>();
            var list = await settings.GetCurrentValue(socket.UserId, "flipBackup", () => new List<BackupEntry>());
            socket.SendMessage(COFLNET + "Restoring settings");
            await socket.sessionLifesycle.FlipSettings.Update(list.Where(l => l.Name == arguments.Trim('"')).Select(l => l.settings).First());
        }
    }
}