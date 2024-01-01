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
        "You can create 3 to 10 backups")]
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
            var hover = (d as SocketDialogBuilder)?.Socket.formatProvider.FormatSettingsSummary(e.settings) + "\nclick to restore these settings";
            return d.Msg($"{Format(e)} {McColorCodes.GREEN}[RESTORE]", $"/cofl restore {GetId(e)}", hover)
                .Msg($"{McColorCodes.LIGHT_PURPLE}[UPDATE]", $"/cofl backup add {GetId(e)}", $"click to update this backup\nwith your current config\n{McColorCodes.RED}Warning: this will overwrite it");
        }
        protected override string GetId(BackupEntry elem)
        {
            return elem.Name;
        }

        protected override async Task<List<BackupEntry>> GetList(MinecraftSocket socket)
        {
            return await GetBackupList(socket);
        }

        protected override async Task AddEntry(MinecraftSocket socket, BackupEntry newEntry)
        {
            var list = await GetList(socket);
            var existing = list.Where(l => GetId(l) == GetId(newEntry)).FirstOrDefault();
            if (existing != null)
            {
                socket.SendMessage(new DialogBuilder()
                    .MsgLine($"Overwriting config {Format(newEntry)}"));
                list.Remove(existing);
            }
            list.Add(newEntry);
            await Update(socket, list);
            var word = existing == null ? "Added" : "Updated";
            socket.SendMessage(new DialogBuilder()
                .MsgLine($"{word} {Format(newEntry)}"));
        }

        public static async Task<List<BackupEntry>> GetBackupList(MinecraftSocket socket)
        {
            var settings = socket.GetService<SettingsService>();
            return await settings.GetCurrentValue(socket.UserId, "flipBackup", () => new List<BackupEntry>());
        }

        protected override async Task Update(MinecraftSocket socket, List<BackupEntry> newCol)
        {
            if (newCol.Count > 3 && await socket.UserAccountTier() < AccountTier.STARTER_PREMIUM)
                throw new CoflnetException("to_many", "you can currently only create 3 different backups, please remove one before creating another. \nYou can get up to 10 with a paid plan");
            if (newCol.Count > 10)
                throw new CoflnetException("to_many", "you can currently only create 10 different backups, please remove one before creating another");
            await SaveBackupList(socket, newCol);
        }

        public static async Task SaveBackupList(MinecraftSocket socket, List<BackupEntry> newCol)
        {
            var settings = socket.GetService<SettingsService>();
            await settings.UpdateSetting(socket.UserId, "flipBackup", newCol);
        }
    }


}