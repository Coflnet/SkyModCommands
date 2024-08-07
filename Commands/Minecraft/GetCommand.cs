using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
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
                    category = o.Key.Substring(0, 3) switch
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
            if (args == "tier")
            {
                var accountTier = await socket.sessionLifesycle.TierManager.GetCurrentTierWithExpire();
                socket.Send(Response.Create("tier", new { accountTier.tier, accountTier.expiresAt }));
                return;
            }
            try
            {
                var service = socket.GetService<SettingsUpdater>();
                var value = service.GetCurrentValue(socket, args);
                socket.SendMessage(new ChatPart($"{COFLNET}{args} is {McColorCodes.AQUA}{value}"));
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