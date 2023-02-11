using System;
using System.Linq;
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

                socket.Send(Response.Create("settings", await Task.WhenAll(updater.ModOptions.Where(o => !o.Value.Hide).Select(async o => new
                {
                    key = o.Key,
                    name = o.Value.RealName,
                    value = await updater.GetCurrentValue(socket, o.Key),
                    info = o.Value.Info,
                    type = o.Value.Type,
                    category = o.Key.Substring(0,3) switch
                    {
                        "mod" => "mod",
                        "sho" => "visibility",
                        "pri" => "privacy",
                        _ => "general"
                    }
                }))));
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