using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.MC
{
    public class GetCommand : McCommand
    {
        private static SettingsUpdater updater = new SettingsUpdater();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            try
            {
                var service = DiHandler.ServiceProvider.GetRequiredService<SettingsService>();
                await service.UpdateSetting("1", "flipSettings", new FlipSettings() { Changer = arguments });
                //socket.SendMessage(new ChatPart($"{COFLNET}val is {socket.SettingsTest.Value.Changer}"));
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