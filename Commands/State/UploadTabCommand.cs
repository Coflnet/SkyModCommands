using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC
{
    public class UploadTabCommand : McCommand
    {
        public override Task Execute(MinecraftSocket socket, string arguments)
        {
            if (arguments.Contains("The Rift"))
                MinecraftSocket.Commands["uploadscoreboard"].Execute(socket, arguments);
            var fields = this.Convert<string[]>(arguments);
            foreach (var item in fields)
            {
                if(item.StartsWith("Profile: "))
                {
                    socket.SessionInfo.ProfileId = item["Profile: ".Length..];
                }
            }
            return Task.CompletedTask;
        }
    }
}