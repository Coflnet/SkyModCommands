using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class ConfigsCommand : ListCommand<ConfigsCommand.ConfigRating, List<ConfigsCommand.ConfigRating>>
{
    protected Dictionary<string, Func<IEnumerable<ConfigRating>, IOrderedEnumerable<ConfigRating>>> sorters = new(){
        {"rating", e => e.OrderByDescending(c => c.Rating)},
        {"rep", e => e.OrderByDescending(c => c.Rating)},
        {"price", e => e.OrderBy(c => c.Price)},
        {"name", e => e.OrderBy(c => c.ConfigName)},
        {"new", e => e.OrderBy(c => c.Created)},
        {"newest", e => e.OrderBy(c => c.Created)},
        {"oldest", e => e.OrderByDescending(c => c.Created)},
        {"updated", e => e.OrderBy(c => c.LastUpdated)},
        {"lastupdated", e => e.OrderBy(c => c.LastUpdated)}
    };
    protected override async Task DefaultAction(MinecraftSocket socket, string stringArgs)
    {
        var args = stringArgs.Split(' ');
        var command = args[0];
        Console.WriteLine($"Command: {command}");
        if (command == "+rep")
        {
            await GiveRep(socket, args);
        }
        else if (command == "-rep")
        {
            await RemoveRep(socket, args);
        }
        else if (command == "autoupdate")
        {
            await ToggleAutoupdate(socket);
        }
        else if (command == "unload")
        {
            await UnloadConfig(socket);
        }
        else if (sorters.TryGetValue(command, out var sorter))
        {
            await PrintSorted(socket, sorter);
            return;
        }
        else
        {
            await base.List(socket, stringArgs);
            socket.SendMessage($"See {McColorCodes.AQUA}/cofl configs help{McColorCodes.GRAY} to see options.");
        }
    }

    private async Task GiveRep(MinecraftSocket socket, string[] args)
    {
        var table = GetTable(socket);
        var targetConfig = await GetTargetRating(socket, args, table);
        if (targetConfig.Upvotes.Contains(socket.UserId))
        {
            socket.SendMessage("You already upvoted this config.");
            return;
        }
        var targetConfigClone = targetConfig.Copy();
        if (targetConfig.Downvotes.Contains(socket.UserId))
        {
            targetConfig.Downvotes.Remove(socket.UserId);
            targetConfig.Rating++;
        }
        targetConfig.Upvotes.Add(socket.UserId);
        targetConfig.Rating++;
        await table.Insert(targetConfig).ExecuteAsync();
        await Delete(table, targetConfigClone);
        socket.Dialog(db => db.MsgLine($"Upvoted §6{targetConfig.ConfigName}"));
    }

    private async Task RemoveRep(MinecraftSocket socket, string[] args)
    {
        var table = GetTable(socket);
        var targetConfig = await GetTargetRating(socket, args, table);
        var targetConfigClone = targetConfig.Copy();
        if (targetConfig.Downvotes.Contains(socket.UserId))
        {
            socket.SendMessage("You already downvoted this config.");
            return;
        }
        if (targetConfig.Upvotes.Contains(socket.UserId))
        {
            targetConfig.Upvotes.Remove(socket.UserId);
            targetConfig.Rating--;
        }
        targetConfig.Downvotes.Add(socket.UserId);
        targetConfig.Rating--;
        await table.Insert(targetConfig).ExecuteAsync();
        await table.Where(c => c.Type == "config" && c.OwnerId == targetConfig.OwnerId && c.ConfigName == targetConfig.ConfigName && c.Rating == targetConfigClone.Rating).Delete().ExecuteAsync();
        socket.Dialog(db => db.MsgLine($"Downvoted §6{targetConfig.ConfigName}"));
    }

    private static async Task ToggleAutoupdate(MinecraftSocket socket)
    {
        var settings = socket.sessionLifesycle.AccountSettings;
        settings.Value.AutoUpdateConfig = !settings.Value.AutoUpdateConfig;
        await settings.Update();
        socket.SendMessage($"Auto update configs is now {McColorCodes.AQUA}{(settings.Value.AutoUpdateConfig ? "enabled" : "disabled")}");
    }

    private static async Task UnloadConfig(MinecraftSocket socket)
    {
        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = null;
        await socket.sessionLifesycle.AccountSettings.Update();
        socket.sessionLifesycle.LoadedConfig?.Dispose();
        socket.sessionLifesycle.LoadedConfig = null;
        await socket.sessionLifesycle.FlipSettings.Update(ModSessionLifesycle.DefaultSettings);
        socket.SendMessage("Unloaded config you won't get updates anymore.");
    }

    private async Task PrintSorted(MinecraftSocket socket, Func<IEnumerable<ConfigRating>, IOrderedEnumerable<ConfigRating>> sorter)
    {
        var elements = await GetList(socket);

        elements = sorter(elements).ToList();

        socket.Dialog(db => db
            .MsgLine($"Sorted results:", $"/cofl {Slug} ls", $"See unsorted result")
            .ForEach(elements.Take(12), (d, e) =>
            {
                ListResponse(d, e);
            }));
    }

    public static async Task Delete(Table<ConfigRating> table, ConfigRating targetConfig)
    {
        await table.Where(c => c.Type == "config" && c.OwnerId == targetConfig.OwnerId && c.ConfigName == targetConfig.ConfigName && c.Rating == targetConfig.Rating).Delete().ExecuteAsync();
    }

    protected override void ListResponse(DialogBuilder d, ConfigRating e)
    {
        FormatForList(d, e).MsgLine($" {McColorCodes.YELLOW}[BUY]{DEFAULT_COLOR}", $"/cofl buyconfig {e.OwnerId} {e.ConfigName}", $"buy {LongFormat(e)}");
    }

    private async Task<ConfigRating> GetTargetRating(MinecraftSocket socket, string[] args, Table<ConfigRating> table)
    {
        var configName = args[2];
        var owner = args[1];
        await table.CreateIfNotExistsAsync();
        var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        if (ownedConfigs.Value.Configs == null)
        {
            throw new CoflnetException("not_found", "You don't own any configs");
        }
        var owned = ownedConfigs.Value.Configs
            .Where(w => w.Name.Equals(configName, System.StringComparison.CurrentCultureIgnoreCase) && (w.OwnerName?.Equals(owner, System.StringComparison.CurrentCultureIgnoreCase) ?? true)).FirstOrDefault();
        if (owned == default)
        {
            throw new CoflnetException("not_found", "You don't own such a config");
        }
        return await GetRatingOrDefault(table, configName, owned);
    }

    public async Task<ConfigRating> GetRatingOrDefault(Table<ConfigRating> table, string configName, OwnedConfigs.OwnedConfig owned)
    {
        var ownerConfigs = await table.Where(c => c.OwnerId == owned.OwnerId).ExecuteAsync();
        var targetConfig = ownerConfigs.Where(c => c.ConfigName.Equals(configName, System.StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
        if (targetConfig == default)
        {
            targetConfig = new ConfigRating()
            {
                ConfigName = configName,
                OwnerId = owned.OwnerId,
                OwnerName = owned.OwnerName,
                Price = owned.PricePaid,
                Type = "config",
                Rating = 0,
                Created = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                Upvotes = new List<string>(),
                Downvotes = new List<string>()
            };
        }
        return targetConfig;
    }

    public Table<ConfigRating> GetTable(IMinecraftSocket socket)
    {
        var mapping = new MappingConfiguration().Define(
            new Map<ConfigRating>()
                .TableName("config_ratings")
                .PartitionKey(c => c.Type)
                .ClusteringKey(c => c.Rating)
                .ClusteringKey(c => c.OwnerId)
                .ClusteringKey(c => c.ConfigName)
                .Column(c => c.ConfigName, cm => cm.WithSecondaryIndex())
                .Column(c => c.OwnerId, cm => cm.WithSecondaryIndex())
        );

        // drop table config_ratings
        return new Table<ConfigRating>(socket.GetService<ISession>(), mapping);
    }

    protected override Task Help(MinecraftSocket socket, string subArgs)
    {
        socket.SendMessage(new DialogBuilder()
            .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} +rep <ign> <config>{DEFAULT_COLOR} upvotes config")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} -rep <ign> <config>{DEFAULT_COLOR} downvotes config")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists available configs")
            .MsgLine($"{McColorCodes.AQUA}/cofl ownconfigs{DEFAULT_COLOR} lists bought configs")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} help{DEFAULT_COLOR} display this help"));

        return Task.CompletedTask;
    }

    protected override Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
    {
        throw new CoflnetException("not_possible", "use the /cl buyconfig command to buy configs");
    }

    protected override Task<List<ConfigRating>> GetList(MinecraftSocket socket)
    {
        // add created and last updated columns
        try
        {
            socket.GetService<ISession>().Execute("ALTER TABLE config_ratings ADD lastupdated timestamp");
            socket.GetService<ISession>().Execute("ALTER TABLE config_ratings ADD created timestamp");
        }
        catch (System.Exception e)
        {
            // already exists
        }
        var table = GetTable(socket);
        return table.Where(c => c.Type == "config").ExecuteAsync().ContinueWith(t => t.Result.ToList());
    }

    protected override Task Update(MinecraftSocket socket, List<ConfigRating> newCol)
    {
        throw new CoflnetException("not_possible", "currently not possible");
    }

    protected override string Format(ConfigRating elem)
    {

        // emoji for upvote and downvote
        return $"§6{elem.ConfigName} §7by {elem.OwnerName} {McColorCodes.GRAY}({McColorCodes.GREEN}⬆{elem.Upvotes.Count} {McColorCodes.RED}⬇{elem.Downvotes.Count}{McColorCodes.GRAY})";
    }

    protected override string GetId(ConfigRating elem)
    {
        return elem.OwnerId + elem.ConfigName;
    }

    public class ConfigRating
    {
        public string Type { get; set; }
        public int Rating { get; set; }
        public string ConfigName { get; set; }
        public string OwnerId { get; set; }
        public string OwnerName { get; set; }
        public int Price { get; set; }
        public DateTime Created { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<string> Upvotes { get; set; }
        public List<string> Downvotes { get; set; }

        public ConfigRating Copy()
        {
            return new ConfigRating()
            {
                Type = Type,
                Rating = Rating,
                ConfigName = ConfigName,
                OwnerId = OwnerId,
                OwnerName = OwnerName,
                Price = Price,
                Created = Created,
                LastUpdated = LastUpdated,
                Upvotes = new List<string>(Upvotes),
                Downvotes = new List<string>(Downvotes)
            };
        }
    }
}
