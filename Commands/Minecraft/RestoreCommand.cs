using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Models;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Restore settings from backup",
        "You probably want to use the restore option in",
        "/cofl backup list instead of this one directly"
        )]
    public class RestoreCommand : McCommand
    {
        public override bool IsPublic => true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var settings = socket.GetService<SettingsService>();
            var list = await settings.GetCurrentValue(socket.UserId, "flipBackup", () => new List<BackupEntry>());
            socket.SendMessage(COFLNET + "Restoring settings");
            var toLoad = list.Where(l => l.Name == arguments.Trim('"')).Select(l => l.settings).FirstOrDefault();
            if (toLoad == null)
                throw new CoflnetException("not_found", "No backup with that name found, try using /cofl backup list to see all backups and use the option to restore from there");
            await socket.sessionLifesycle.FlipSettings.Update(toLoad);
        }
    }
}