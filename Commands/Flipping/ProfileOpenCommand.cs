using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

public class ProfileOpenCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        await socket.TryAsyncTimes(async () =>
        {
            var name = await GetMcNameForCommand.GetName(socket, arguments);
            using var span = socket.CreateActivity("profileopen", socket.ConSpan);
            span.SetTag("name", name);
            socket.ExecuteCommand($"/pv {name}");
            socket.Dialog(db => db.MsgLine($"Opened profile for §6{name}\n{McColorCodes.RESET}If nothing opens you can click this to put the name in chat", "suggest:/pv" + name, "Click to put the name in chat")
                .LineBreak().MsgLine("Or open on skycrypt", "https://sky.shiiyu.moe/stats/" + name));
        }, "opening player profile");
    }
}