using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public class GetCommand : McCommand
    {
        private static SettingsUpdater updater = new SettingsUpdater();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string>(arguments);
            if (args == "json")
            {
                var lifeCycle = socket.sessionLifesycle;
                socket.Send(Response.Create("settings", new { flip = lifeCycle.FlipSettings.Value, privacy = lifeCycle.PrivacySettings.Value }));
                await Task.Delay(500);
                return;
            }
            try
            {
                //var service = DiHandler.ServiceProvider.GetRequiredService<SettingsService>();
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