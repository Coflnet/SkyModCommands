using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "A list of the top configs",
    "Allows you to see the most popular configs",
    "You can upvote and downvote configs",
    "You can also see stats for each config",
    "and buy them if you don't own them yet",
    "Note that configs are not required to use the flipper")]
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
        else if (command == "stats")
        {
            await GetStats(socket, args);
        }
        else if (command == "autoupdate")
        {
            await ToggleAutoupdate(socket);
        }
        else if (command == "unload")
        {
            await UnloadAndResetConfig(socket);
        }
        else if (command == "reset")
        {
            await Reset(socket);
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

    private async Task GetStats(MinecraftSocket socket, string[] args)
    {
        var configName = args[2];
        var owner = args[1];

        if (!int.TryParse(owner, out _))
        {
            owner = await GetUserIdFromMcName(socket, owner);
        }

        var service = socket.GetService<ConfigStatsService>();
        var loads = (await service.GetLoads(owner, configName)).ToList();
        if (loads.Count == 0)
        {
            socket.Dialog(db => db.MsgLine($"Nobody used {McColorCodes.GOLD}{configName}{McColorCodes.GRAY} in the last 2 days")
                .MsgLine($"{McColorCodes.DARK_GRAY}Stats are computed on the last 2 days only"));
            return;
        }
        var differentUsers = loads.Select(l => l.UserId).Distinct().Count();
        var uuids = loads.Select(l => l.McUuid).Distinct().Where(l => Guid.TryParse(l, out _)).ToList();
        var timeSpan = TimeSpan.FromDays(2);
        var flips = await socket.GetService<FlipTrackingService>().GetPlayerFlips(uuids, timeSpan);
        var differentSellers = flips.Flips.Select(f => f.Seller).Distinct().Count();
        var flipCount = flips.Flips.Length;
        var flipProfit = flips.Flips.Sum(f => f.Profit);
        var flipPaid = flips.Flips.Sum(f => f.PricePaid);
        Console.WriteLine($"Flips: {flipCount} Profit: {flipProfit} Paid: {flipPaid} - count {uuids.Count()} ({differentSellers}) - {timeSpan}");
        var avgProfitPerDay = flipProfit / Math.Max(differentSellers, 1) / timeSpan.TotalDays;
        var flipsPerDay = flipCount / differentUsers / timeSpan.TotalDays;
        var mostCommonItem = flips.Flips.GroupBy(f => f.ItemTag).OrderByDescending(g => g.Count()).FirstOrDefault()?.First().ItemName ?? "none";
        var mostProfit = flips.Flips.OrderByDescending(f => f.Profit).FirstOrDefault();

        socket.Dialog(db => db
            .MsgLine($"Stats for {McColorCodes.GOLD}{configName}")
            .MsgLine($" Loaded {McColorCodes.AQUA}{loads.Count}{McColorCodes.RESET} times by {McColorCodes.AQUA}{differentUsers}{McColorCodes.RESET} different users")
            .MsgLine($" On average users flipped {McColorCodes.AQUA}{(int)flipsPerDay}{McColorCodes.RESET} items and profited {McColorCodes.GOLD}{socket.FormatPrice(avgProfitPerDay)} per day")
            .MsgLine($" In total {McColorCodes.AQUA}{socket.FormatPrice(flipPaid)}{McColorCodes.RESET} coins were spent")
            .MsgLine($" The most common flipped item was {McColorCodes.GOLD}{mostCommonItem}")
            .If(() => mostProfit != null, db => db.MsgLine($"The most profitable flip was a {ProfitCommand.FormatFlipName(socket, mostProfit)} {ProfitCommand.FormatFlip(socket, mostProfit)}"))
            .MsgLine($"{McColorCodes.DARK_GRAY} Note that users may have changed config... ", null,
                "Note that users may have changed config throughout \nthe time period which is not accounted for"));
    }

    private async Task Reset(MinecraftSocket socket)
    {
        await Unloadconfig(socket);
        await socket.sessionLifesycle.FlipSettings.Update(ModSessionLifesycle.DefaultSettings);
        socket.Dialog(db => db.MsgLine("Reset config to default."));
    }

    private async Task GiveRep(MinecraftSocket socket, string[] args)
    {
        var table = GetTable();
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
        var table = GetTable();
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

    private static async Task UnloadAndResetConfig(MinecraftSocket socket)
    {
        await Unloadconfig(socket);
        await socket.sessionLifesycle.FlipSettings.Update(ModSessionLifesycle.DefaultSettings);
        socket.SendMessage("Unloaded config you won't get updates anymore.");
        socket.Dialog(db => db.MsgLine($"If you want to reset it to the default do {McColorCodes.AQUA}/cofl configs reset", "/cofl configs reset", "reset config"));
    }

    public static async Task Unloadconfig(MinecraftSocket socket)
    {
        socket.sessionLifesycle.AccountSettings.Value.LoadedConfig = null;
        await socket.sessionLifesycle.AccountSettings.Update();
        var state = socket.sessionLifesycle.FilterState;
        state.LoadedConfig?.Dispose();
        state.LoadedConfig = null;
        state.BaseConfig?.Dispose();
        state.BaseConfig = null;
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
        FormatForList(d, e)
            .Msg($" {McColorCodes.GRAY}[{McColorCodes.YELLOW}STATS{McColorCodes.GRAY}]{DEFAULT_COLOR}", $"/cofl {Slug} stats {e.OwnerId} {e.ConfigName}", $"get {McColorCodes.BOLD}stats{McColorCodes.RESET} for {LongFormat(e)}")
            .MsgLine($" {McColorCodes.GRAY}[{McColorCodes.YELLOW}BUY{McColorCodes.GRAY}]{DEFAULT_COLOR}", $"/cofl buyconfig {e.OwnerId} {e.ConfigName}", $"buy {LongFormat(e)}");
    }

    protected override DialogBuilder FormatForList(DialogBuilder d, ConfigRating elem)
    {
        return d.Msg($"§6{elem.ConfigName} §7by {elem.OwnerName} {McColorCodes.GRAY}(", null, $"{McColorCodes.GRAY}Last updated {FormatProvider.FormatTimeGlobal(DateTime.UtcNow - elem.LastUpdated)} ago")
            .CoflCommand<ConfigsCommand>($"{McColorCodes.GREEN}⬆{elem.Upvotes.Count} ", $"+rep {elem.OwnerId} {elem.ConfigName}", "upvote")
            .CoflCommand<ConfigsCommand>($"{McColorCodes.RED}⬇{elem.Downvotes.Count}", $"-rep {elem.OwnerId} {elem.ConfigName}", "downvote")
            .Msg($"{McColorCodes.GRAY})");
    }

    private async Task<ConfigRating> GetTargetRating(MinecraftSocket socket, string[] args, Table<ConfigRating> table)
    {
        var configName = args[2];
        var owner = args[1];
        await table.CreateIfNotExistsAsync();
        using var ownedConfigs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        if (ownedConfigs.Value.Configs == null)
        {
            throw new CoflnetException("not_found", "You don't own any configs");
        }
        var owned = ownedConfigs.Value.Configs
            .Where(w => w.Name.Equals(configName, System.StringComparison.CurrentCultureIgnoreCase)
                && (w.OwnerId == owner || (w.OwnerName?.Equals(owner, System.StringComparison.CurrentCultureIgnoreCase) ?? true))).FirstOrDefault();
        if (owned == default)
        {
            throw new CoflnetException("not_found", "You don't own such a config");
        }
        if (owned.PricePaid == 0)
        {
            var key = SellConfigCommand.GetKeyFromname(configName);
            var userId = await GetUserIdFromMcName(socket, owner);
            using var toLoad = await SelfUpdatingValue<ConfigContainer>.Create(userId, key, () => null);
            if (toLoad?.Value?.Price > 0)
            {
                var rating = await GetRatingOrDefault(table, configName, owned);
                if (rating.Upvotes.Remove(socket.UserId))
                {
                    rating.Rating--;
                    await table.Insert(rating).ExecuteAsync();
                }
                throw new CoflnetException("not_found", "You didn't buy the config so you can't vote on it");
            }
        }
        return await GetRatingOrDefault(table, configName, owned);
    }

    public async Task<ConfigRating> GetRatingOrDefault(Table<ConfigRating> table, string configName, OwnedConfigs.OwnedConfig owned)
    {
        var ownerConfigs = await table.Where(c => c.OwnerId == owned.OwnerId).ExecuteAsync();
        var targetConfig = ownerConfigs.Where(c => c.ConfigName.Equals(configName, System.StringComparison.CurrentCultureIgnoreCase)).OrderBy(c => c.Downvotes.Count + c.Upvotes.Count).FirstOrDefault();
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

    public Table<ConfigRating> GetTable()
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
        return new Table<ConfigRating>(DiHandler.GetService<ISession>(), mapping);
    }

    protected override Task Help(MinecraftSocket socket, string subArgs)
    {
        socket.SendMessage(new DialogBuilder()
            .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} +rep <ign> <config>{DEFAULT_COLOR} upvotes config")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} -rep <ign> <config>{DEFAULT_COLOR} downvotes config")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} stats <ign> <config>{DEFAULT_COLOR} get usage stats")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists available configs")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} autoupdate{DEFAULT_COLOR} toggles autoupdate")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} reset{DEFAULT_COLOR} resets config to default")
            .MsgLine($"{McColorCodes.AQUA}/cofl ownconfigs{DEFAULT_COLOR} lists bought configs")
            .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} help{DEFAULT_COLOR} display this help"));

        return Task.CompletedTask;
    }

    protected override Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
    {
        throw new CoflnetException("not_possible", "use the /cl buyconfig command to buy configs");
    }

    protected override async Task<List<ConfigRating>> GetList(MinecraftSocket socket)
    {
        var table = GetTable();
        var content = await table.Where(c => c.Type == "config").ExecuteAsync();
        return content.OrderByDescending(c => c.Rating).ToList();
    }

    protected override Task Update(MinecraftSocket socket, List<ConfigRating> newCol)
    {
        throw new CoflnetException("not_possible", "currently not possible");
    }

    protected override string Format(ConfigRating elem)
    {
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
