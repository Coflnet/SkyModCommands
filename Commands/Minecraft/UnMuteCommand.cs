using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class UnMuteCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var name = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
            var settings = socket.sessionLifesycle.AccountSettings;
            if (settings == null)
            {
                socket.SendMessage($"You have to be logged in to do this");
                return;
            }
            if(string.IsNullOrEmpty(name))
            {
                socket.SendMessage($"Please specify an user name to unmute");
                return;
            }
            var val = settings.Value;

            var uuid = await socket.GetPlayerUuid(name);
            
            if (val.MutedUsers == null)
                val.MutedUsers = new System.Collections.Generic.HashSet<UserMute>();
            val.MutedUsers.Remove(new UserMute(uuid, name));
            await settings.Update(val);

            socket.SendMessage(COFLNET + $"You unmuted {name}");
        }
    }
}