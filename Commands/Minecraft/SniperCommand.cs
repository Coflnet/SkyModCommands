using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class FastCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            await socket.UpdateSettings(settings =>
            {
                settings.Settings.FastMode = true;
                return settings;
            });
            hypixel.FlipperService.Instance.AddConnectionPlus(socket,false);
            socket.SendMessage(COFLNET + $"You enabled the fast mode, some settings don't take affect anymore");
        }
    }
}