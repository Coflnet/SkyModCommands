using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

public class RemoveConfigCommand : ArgumentsCommand
{
    protected override string Usage => "<name> [ownerId=0]";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var name = args["name"];
        var ownerId = args["ownerId"];
        if (ownerId == null || ownerId == "0")
        {
            ownerId = socket.UserId;
        }
        else if (!socket.GetService<ModeratorService>().IsModerator(socket))
        {
            socket.Dialog(db => db.Msg("You need to be a moderator to remove other peoples configs."));
            return;
        }
        var configsCommand = MinecraftSocket.Commands.GetBy<ConfigsCommand>();
        var table = configsCommand.GetTable();
        var rating = await configsCommand.GetRatingOrDefault(table, name, new()
        {
            OwnerId = ownerId
        });
        string key = SellConfigCommand.GetKeyFromname(name);
        using var container = await SelfUpdatingValue<ConfigContainer>.Create(
            ownerId, key, () => null);
        if (container.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        container.Value.Delisted = true;
        container.Value.ModeratorDelisted = ownerId != socket.UserId;
        await container.Update();
        await ConfigsCommand.Delete(configsCommand.GetTable(), rating);
        socket.Dialog(db => db.MsgLine(
            $"§6{name} §7delisted; existing recipients keep their supplied version and managed updates"));
    }
}
