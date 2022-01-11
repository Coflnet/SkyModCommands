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
                arguments = JsonConvert.DeserializeObject<string>(arguments).Replace('$','§');
                var name = arguments.Split(' ')[0];
                if (arguments.Length == 0)
                {
                    socket.SendMessage(COFLNET + "Available settings are:\n" + String.Join(",\n", updater.Options()));
                    return;
                }
                var newValue = arguments.Substring(name.Length + 1);
                await updater.Update(socket, name, newValue);
                await service.UpdateSetting(socket.UserId.ToString(), "flipSettings", socket.Settings);
                socket.LatestSettings.Settings.Changer = "mod-" + socket.sessionInfo.sessionId;
                await socket.UpdateSettings(current =>
                    current
                );
                socket.SendMessage(new ChatPart($"{COFLNET}Set {McColorCodes.AQUA}{name}{DEFAULT_COLOR} to{McColorCodes.WHITE}{newValue}"));
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