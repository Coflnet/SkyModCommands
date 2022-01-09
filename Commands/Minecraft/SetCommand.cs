using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using hypixel;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class SetCommand : McCommand
    {
        private static SettingsUpdater updater = new SettingsUpdater();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            try
            {
                var service = DiHandler.ServiceProvider.GetRequiredService<SettingsService>();
                if (arguments.Length > 300)
                    throw new CoflnetException("to_long", "the settings value is too long");
                arguments = arguments.Trim('"').Replace('$','§');
                var name = arguments.Split(' ')[0];
                if (arguments.Length == 0)
                {
                    socket.SendMessage("Available settings are:\n" + String.Join(',', updater.Options()));
                    return;
                }
                await updater.Update(socket, name, arguments.Substring(name.Length + 1));
                await service.UpdateSetting(socket.UserId.ToString(), "flipSettings", socket.Settings);
                socket.LatestSettings.Settings.Changer = "mod-" + socket.sessionInfo.sessionId;
                await socket.UpdateSettings(current =>
                    current
                );
            }
            catch (CoflnetException e)
            {
                socket.SendMessage(new ChatPart(COFLNET + e.Message));
                dev.Logger.Instance.Error(e, "set setting");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "set setting");
            }
        }
    }
}