using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Linq;
using static Coflnet.Sky.Commands.Shared.SettingsUpdater;

namespace Coflnet.Sky.Commands.MC
{
    public class SetCommand : McCommand
    {
        private static SettingsUpdater updater = new SettingsUpdater();
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            try
            {
                if (arguments.Length > 300)
                    throw new CoflnetException("to_long", "the settings value is too long");
                arguments = JsonConvert.DeserializeObject<string>(arguments).Replace('$', '§');
                var name = arguments.Split(' ')[0];
                int page = 0;
                if (arguments.Length == 0 || int.TryParse(arguments.Split(' ')[0], out page))
                {
                    var pageSize = 12;
                    page++;
                    Func<string, string> formatSh = v => MC.McColorCodes.GRAY + " (" + MC.McColorCodes.GREEN + v + MC.McColorCodes.GRAY + ")";
                    var options = updater.ModOptions.Where(o => !o.Value.Hide).Select(o =>
                    {
                        var shortHandAddition = !string.IsNullOrEmpty(o.Value.ShortHand) ? formatSh(o.Value.ShortHand) : "";
                        return $"{MC.McColorCodes.AQUA}{o.Key}{shortHandAddition}: {MC.McColorCodes.GRAY}{o.Value.Info}";
                    });
                    socket.SendMessage($"{COFLNET}Available settings are (page {page}):\n" + String.Join("\n", options.Skip(page * pageSize).Take(pageSize)),
                        "/cofl set " + (page + 1),
                        $"These are available settings, the format is:\n{MC.McColorCodes.AQUA}key{formatSh("shortVersion")}{MC.McColorCodes.GRAY} Description\n"
                        + "click to get next page");
                    return;
                }
                var newValue = arguments.Substring(name.Length).Trim();
                object finalValue;
                try
                {
                    finalValue = await updater.Update(socket, name, newValue);
                }
                catch (UnkownSettingException e)
                {
                    throw new CoflnetException(e.Slug, $"the setting {e.Passed} doesn't exist, most similar is {McColorCodes.AQUA}{e.Closest}{DEFAULT_COLOR}");
                } 
                //socket.LatestSettings.Settings.Changer = "mod-" + socket.SessionInfo.sessionId;
                if (string.IsNullOrEmpty(socket.sessionLifesycle.UserId))
                    socket.SendMessage(new ChatPart($"{COFLNET}You are not logged in, setting will reset when you stop the connection"));
                else
                {
                    var service = DiHandler.ServiceProvider.GetRequiredService<SettingsService>();
                    await service.UpdateSetting(socket.sessionLifesycle.UserId, "flipSettings", socket.Settings);
                }
                var doc = updater.GetDocFor(name);
                socket.SendMessage(new ChatPart($"{COFLNET}Set {McColorCodes.AQUA}{doc.RealName}{DEFAULT_COLOR} to {McColorCodes.WHITE}{finalValue}", null, doc.Info));
            }
            catch (CoflnetException e)
            {
                socket.SendMessage(new ChatPart(COFLNET + e.Message));
                dev.Logger.Instance.Error(e, "set setting");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "set setting");
                socket.SendMessage(new ChatPart(COFLNET + "an error occured while executing that"));

            }
        }
    }
}