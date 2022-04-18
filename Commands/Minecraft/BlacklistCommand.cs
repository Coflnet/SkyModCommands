using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public class BlacklistCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var tag = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
            var settings = socket.sessionLifesycle.FlipSettings;
            if(settings.Value == null)
                throw new Coflnet.Sky.Core.CoflnetException("login","Login is required to use this command");
            if(settings.Value.BlackList == null)
                settings.Value.BlackList = new System.Collections.Generic.List<ListEntry>();
            settings.Value.BlackList.Add(new ListEntry() { ItemTag = tag });
            await settings.Update(settings.Value);
            socket.SendMessage(COFLNET + $"You blacklisted all {arguments} from appearing");
        }
    }
}