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
        var table = configsCommand.GetTable(socket);
        var rating = await configsCommand.GetRatingOrDefault(table, name, new()
        {
            OwnerId = ownerId
        });
        if (rating.Downvotes.Count > 0 || rating.Upvotes.Count > 0)
        {
            socket.Dialog(db => db.Msg("You can't remove a config that has been voted on."));
            return;
        }
        await ConfigsCommand.Delete(configsCommand.GetTable(socket), rating);
        string key = SellConfigCommand.GetKeyFromname(name);
        await SelfUpdatingValue<ConfigContainer>.Create(socket.UserId, key);
        var settingsService = socket.GetService<SettingsService>();
        await settingsService.UpdateSetting(ownerId, key, new ConfigContainer());
        using var createdConfigs = await SelfUpdatingValue<CreatedConfigs>.Create(ownerId, "created_configs", () => new());
        createdConfigs.Value.Configs.Remove(name);
        await createdConfigs.Update();
        using var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(ownerId, "owned_configs", () => new());
        ownedConfigs.Value.Configs.RemoveAll(c => c.Name == name && c.OwnerId == ownerId);
        await ownedConfigs.Update();
        socket.Dialog(db => db.MsgLine($"§6{name} §7removed"));
    }
}