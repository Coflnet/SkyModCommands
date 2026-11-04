using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using fNbt;
using fNbt.Tags;
using Item = Coflnet.Sky.Core.Services.Item;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists the cheapest missing accessories per magical power",
    "Reads your accessory bag from the profile api and",
    "from accessory bag pages you opened with the mod.",
    "Upgrades only count the power gained over what you own")]
public class CheapAccessoriesCommand : ReadOnlyListCommand<CheapAccessoriesCommand.Cheapest>
{
    public override bool IsPublic => true;
    protected override string Title => "Cheapest accessories per magical power";

    private static readonly Dictionary<string, int> PowerByTier = new()
    {
        { "COMMON", 3 },
        { "UNCOMMON", 5 },
        { "RARE", 8 },
        { "EPIC", 12 },
        { "LEGENDARY", 16 },
        { "MYTHIC", 22 },
        { "SPECIAL", 3 },
        { "VERY_SPECIAL", 5 }
    };

    public class Cheapest
    {
        public string Tag { get; set; }
        public string ItemName { get; set; }
        public int PowerGain { get; set; }
        public long Price { get; set; }
        public long PricePerPower { get; set; }
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, Cheapest item)
    {
        db.MsgLine($" {item.ItemName} for {McColorCodes.AQUA}{socket.FormatPrice(item.PricePerPower)} coins {McColorCodes.GRAY}per magical power",
            "/ahs " + item.ItemName,
            $"{McColorCodes.YELLOW}Click to search the auction house\n" +
            $"{McColorCodes.GRAY}Lowest bin {socket.FormatPrice(item.Price)} for +{item.PowerGain} magical power");
    }

    protected override async Task<IEnumerable<Cheapest>> GetElements(MinecraftSocket socket, string val)
    {
        var itemsTask = socket.GetService<IHypixelItemStore>().GetItemsAsync();
        var pricesTask = socket.GetService<Sniper.Client.Api.IAuctionApi>().ApiAuctionLbinsGetAsync();
        var craftsTask = socket.GetService<ICraftsApi>().GetAllAsync();
        var owned = await GetOwnedAccessories(socket);
        if (owned.Count == 0)
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}No accessories found, do you have inventory api on? {McColorCodes.GRAY}You can also open your accessory bag"));
        else
            socket.Dialog(db => db.MsgLine($"Skipping {owned.Count} accessories you already have"));
        var prices = (await pricesTask).ToDictionary(p => p.Key, p => p.Value?.Price ?? 0);
        var amount = socket.SessionInfo.SessionTier switch
        {
            AccountTier.PREMIUM_PLUS => 1000,
            AccountTier.PREMIUM => 500,
            _ => 100
        };
        return Rank(await itemsTask, prices, await craftsTask, owned).Take(amount);
    }

    /// <summary>
    /// Orders not yet owned accessories by lowest bin per magical power gained.
    /// An accessory whose recipe contains another accessory is treated as its upgrade.
    /// </summary>
    public static IEnumerable<Cheapest> Rank(Dictionary<string, Item> items, Dictionary<string, long> prices, IEnumerable<ProfitableCraft> crafts, HashSet<string> owned)
    {
        var accessories = items.Where(i => i.Value.Category == "ACCESSORY").ToDictionary(i => i.Key, i => i.Value);
        var lowerTiers = GetLowerTiers(accessories, crafts);
        var outgrown = owned.SelectMany(o => GetTransitive(o, lowerTiers)).ToHashSet();
        var result = new List<Cheapest>();
        foreach (var (tag, item) in accessories)
        {
            if (!prices.TryGetValue(tag, out var price) || price <= 0 || owned.Contains(tag) || outgrown.Contains(tag))
                continue;
            var replaced = GetTransitive(tag, lowerTiers).Where(owned.Contains).Select(t => Power(accessories[t])).DefaultIfEmpty(0).Max();
            var gain = Power(item) - replaced;
            if (gain <= 0)
                continue;
            result.Add(new Cheapest { Tag = tag, ItemName = item.Name, PowerGain = gain, Price = price, PricePerPower = price / gain });
        }
        return result.OrderBy(r => (double)r.Price / r.PowerGain).ThenBy(r => r.Price);
    }

    private static int Power(Item item)
    {
        return PowerByTier.GetValueOrDefault(item.Tier ?? "COMMON", 0);
    }

    private static Dictionary<string, HashSet<string>> GetLowerTiers(Dictionary<string, Item> accessories, IEnumerable<ProfitableCraft> crafts)
    {
        return (crafts ?? []).Where(c => c?.ItemId != null && accessories.ContainsKey(c.ItemId))
            .GroupBy(c => c.ItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(c => c.Ingredients ?? [])
                .Select(i => i.ItemId).Where(id => id != null && id != g.Key && accessories.ContainsKey(id)).ToHashSet());
    }

    private static HashSet<string> GetTransitive(string tag, Dictionary<string, HashSet<string>> lowerTiers)
    {
        var found = new HashSet<string>();
        var queue = new Queue<string>([tag]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var lower in lowerTiers.GetValueOrDefault(current) ?? [])
                if (lower != tag && found.Add(lower))
                    queue.Enqueue(lower);
        }
        return found;
    }

    private static async Task<HashSet<string>> GetOwnedAccessories(MinecraftSocket socket)
    {
        var owned = new HashSet<string>();
        owned.UnionWith(await TryGet(() => GetFromProfile(socket)));
        owned.UnionWith(await TryGet(() => GetFromOpenedBag(socket)));
        Activity.Current?.Log("Owned accessories: " + string.Join(", ", owned));
        return owned;
    }

    private static async Task<IEnumerable<string>> TryGet(Func<Task<IEnumerable<string>>> source)
    {
        try
        {
            return await source();
        }
        catch (Exception e)
        {
            Activity.Current?.Log("Accessory source failed: " + e.Message);
            return [];
        }
    }

    private static async Task<IEnumerable<string>> GetFromProfile(MinecraftSocket socket)
    {
        var profileClient = socket.GetService<IProfileClient>();
        var profileId = string.IsNullOrEmpty(socket.SessionInfo.ProfileId) ? "current" : socket.SessionInfo.ProfileId;
        var member = await profileClient.GetProfile(socket.SessionInfo.McUuid, profileId, DateTime.UtcNow - TimeSpan.FromMinutes(10));
        var data = member?.Inventory?.BagContents?.TalismanBag?.Data;
        if (string.IsNullOrEmpty(data))
            return [];
        var list = NBT.File(System.Convert.FromBase64String(data)).RootTag.Get<NbtList>("i");
        return list?.OfType<NbtCompound>().Select(NBT.ItemID).Where(id => id != null).ToList() ?? [];
    }

    private static async Task<IEnumerable<string>> GetFromOpenedBag(MinecraftSocket socket)
    {
        var stateApi = socket.GetService<IPlayerStateApi>();
        var storage = await stateApi.PlayerStatePlayerIdStorageGetAsync(Guid.Parse(socket.SessionInfo.McUuid), Guid.Empty);
        return storage.Where(c => c.Name?.StartsWith("Accessory Bag") ?? false)
            .SelectMany(c => c.Items ?? []).Select(i => i?.Tag).Where(t => t != null).ToList();
    }

    protected override string GetId(Cheapest elem)
    {
        return elem.ItemName;
    }
}
