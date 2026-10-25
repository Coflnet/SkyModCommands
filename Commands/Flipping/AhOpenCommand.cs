using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.MC;

public class AhOpenCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        await socket.TryAsyncTimes(async () =>
        {
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string name = null;
            name = await GetMcNameForCommand.GetName(socket, arguments, async currentName =>
            {
                // the rename check runs in the background, reopen only after the stored name was opened
                await opened.Task;
                if (socket.IsClosed)
                    return;
                socket.Dialog(db => db.MsgLine($"{McColorCodes.YELLOW}{name} changed their name to {McColorCodes.AQUA}{currentName}{McColorCodes.YELLOW}, opening their auction house again"));
                OpenAh(socket, currentName);
            });
            OpenAh(socket, name);
            opened.SetResult();
        }, "opening player ah");
    }

    private static void OpenAh(MinecraftSocket socket, string name)
    {
        using var span = socket.CreateActivity("ahopen", socket.ConSpan);
        span.SetTag("name", name);
        socket.ExecuteCommand($"/ah {name}");
    }
}
