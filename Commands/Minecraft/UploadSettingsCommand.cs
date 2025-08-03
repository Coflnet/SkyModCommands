using System.Threading.Tasks;
using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

public class UploadSettingsCommand : McCommand
{
    private static SettingsUpdater updater = new SettingsUpdater();
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var settings = Convert<Dictionary<string, string>>(arguments);
        var service = socket.GetService<SettingsService>();
        foreach (var setting in settings)
        {
            var current = await updater.GetCurrentValue(socket, setting.Key);
            if (current?.ToString() != setting.Value)
            {
                await updater.Update(socket, setting.Key, setting.Value);
                socket.Dialog(db => db.Msg($"Updated {setting.Key} to {setting.Value}", null, $"From {current}"));
            }
        }
        socket.Settings.Changer = socket.SessionInfo.ConnectionId;
        socket.Settings.LastChanged = "mod";
        await service.UpdateSetting(socket.sessionLifesycle.UserId, "flipSettings", socket.Settings);
    }
}
