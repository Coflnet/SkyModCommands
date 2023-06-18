using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Models;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Create a backup of your settings", 
        "to create use /cofl backup add <name>",
        "to restore use /cofl restore <name>", 
        "You can create up to 3 backups")]
    public class BackupCommand : ListCommand<BackupEntry, List<BackupEntry>>
    {
        public override bool IsPublic => true;
        protected override Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
        {
            return Task.FromResult(new CreationOption[]{new (){Element = new BackupEntry(){
                Name = val,
                settings = socket.Settings
            }}}.AsEnumerable());
        }

        protected override string Format(BackupEntry elem)
        {
            return $"{McColorCodes.AQUA}{elem.Name}{McColorCodes.GRAY} from {elem.CreationDate.ToString("yyyy-MMM-dd")}";
        }

        protected override DialogBuilder FormatForList(DialogBuilder d, BackupEntry e)
        {
            return d.Msg($"{Format(e)} {McColorCodes.GREEN}[RESTORE]", $"/cofl restore {GetId(e)}", "click to restore these settings");
        }
        protected override string GetId(BackupEntry elem)
        {
            return elem.Name;
        }

        protected override async Task<List<BackupEntry>> GetList(MinecraftSocket socket)
        {
            var settings = socket.GetService<SettingsService>();
            return await settings.GetCurrentValue(socket.UserId, "flipBackup", () => new List<BackupEntry>());
        }

        protected override async Task Update(MinecraftSocket socket, List<BackupEntry> newCol)
        {
            if(newCol.Count > 3)
                throw new CoflnetException("to_many", "you can currently only create 3 different backups, please remove one before creating another");
            var settings = socket.GetService<SettingsService>();
            await settings.UpdateSetting(socket.UserId, "flipBackup", newCol);
        }

    }


}