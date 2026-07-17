using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;
using Coflnet.Sky.Commands.Shared;
using Item = Coflnet.Sky.PlayerState.Client.Model.Item;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Shows breakdown of cost for items applied to the main item.",
    "This command allows you to see the total cost of crafting an item",
    "It will show you the total cost and the individual costs of each component",
    "This represents the induvidual costs in TotalCraftCost in lore",
    "Craftable items also show a tree of which ingredients were sub-crafted vs bought")]
public class CraftBreakDownCommand : ItemSelectCommand<CraftBreakDownCommand>
{
    /// <summary>
    /// How deep the sub-craft tree is allowed to recurse to avoid runaway output.
    /// </summary>
    private const int MaxTreeDepth = 8;

    public override bool IsPublic => true;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        await HandleSelectionOrDisplaySelect(socket, args, null, $"Select item to get the cost for \n");
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string context, Item item)
    {
        // hack convert
        var converted = JsonConvert.DeserializeObject<Api.Client.Model.ItemRepresent>(JsonConvert.SerializeObject(item));
        Activity.Current.Log(JsonConvert.SerializeObject(converted));
        var breakdownTask = socket.GetService<IModApi>().ApiModPricingBreakdownPostAsync(new() { converted });
        var craftTreeTask = BuildCraftTree(socket, item.Tag);
        var result = await breakdownTask;
        var craftTree = await craftTreeTask;
        socket.Dialog(db =>
        {
            db.MsgLine("Breakdown:").ForEach(result.First().CraftPrice.GroupBy(c => c.Attribute).OrderBy(g => g.Sum(a => a.Price)), (db, r) =>
                db.MsgLine($" {McColorCodes.YELLOW}{r.Key} {McColorCodes.GRAY}costs {McColorCodes.GOLD}{socket.formatProvider.FormatPrice(r.Sum(c => c.Price))} coins", null,
                string.Join("\n", r.Select(c => NewMethod(socket, c)).Prepend("Required items summed:"))))
                .MsgLine($"Total cost: {McColorCodes.GOLD}{socket.formatProvider.FormatPrice(result.First().CraftPrice.Sum(c => c.Price))} coins");
            RenderCraftTree(socket, db, craftTree);
            return db;
        });

        static string NewMethod(MinecraftSocket socket, Api.Client.Model.CraftPrice c)
        {
            if (c.Price < 0)
            {
                return $"{McColorCodes.RED}{c.FormattedReson}{McColorCodes.GRAY} for {McColorCodes.GOLD}{McColorCodes.ITALIC}0/unknown coins";
            }
            return $"{McColorCodes.YELLOW}{c.FormattedReson}{McColorCodes.GRAY} for {McColorCodes.GOLD}{socket.formatProvider.FormatPrice(c.Price)} coins";
        }
    }

    /// <summary>
    /// A single node in the sub-craft breakdown tree.
    /// </summary>
    private class CraftNode
    {
        public string Tag;
        public long Count;
        public double Cost;
        /// <summary>How this ingredient is obtained: "craft", "npc" or "buy".</summary>
        public string Method;
        public int Depth;
        /// <summary>True when this node was sub-crafted and its ingredients are listed below it.</summary>
        public bool Expanded;
    }

    private class CraftTree
    {
        public ProfitableCraft Root;
        public List<CraftNode> Nodes;
        public Dictionary<string, ItemInfo> Names;
    }

    private record ItemInfo(string Name, string Color, bool IsBazaar);

    /// <summary>
    /// Fetches the craft data and, if the selected item is craftable, builds the flattened
    /// sub-craft tree. Ingredients that were themselves crafted (because that was cheaper than
    /// buying them) are recursively expanded so the user can see whether e.g. null spheres were
    /// crafted from obsidian or bought directly.
    /// </summary>
    private static async Task<CraftTree> BuildCraftTree(MinecraftSocket socket, string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        try
        {
            var craftApi = socket.GetService<ICraftsApi>();
            var itemsTask = socket.GetService<IItemsApi>().ItemsGetAsync();
            var allCrafts = await craftApi.GetAllAsync();
            var lookup = new Dictionary<string, ProfitableCraft>();
            foreach (var craft in allCrafts)
                if (craft?.ItemId != null)
                    lookup[craft.ItemId] = craft;
            if (!lookup.TryGetValue(tag, out var root) || root.Ingredients == null)
                return null;

            var names = (await itemsTask)
                .GroupBy(i => i.Tag).Select(g => g.First())
                .ToDictionary(i => i.Tag, i => new ItemInfo(
                    i.Name ?? i.Tag,
                    socket.formatProvider.GetRarityColor((Core.Tier)i.Tier),
                    i.Flags.HasValue && i.Flags.Value.HasFlag(Items.Client.Model.ItemFlags.BAZAAR)));

            var nodes = new List<CraftNode>();
            AddIngredients(nodes, root, 1, 0, lookup, new HashSet<string> { tag });
            return new CraftTree { Root = root, Nodes = nodes, Names = names };
        }
        catch (Exception e)
        {
            socket.Error(e, "building craft breakdown tree");
            return null;
        }
    }

    /// <summary>
    /// Recursively appends the ingredients of <paramref name="craft"/> to <paramref name="nodes"/>.
    /// <paramref name="multiplier"/> is how many batches of the current craft are needed so the
    /// counts and costs of nested ingredients scale to the amount actually required.
    /// </summary>
    private static void AddIngredients(List<CraftNode> nodes, ProfitableCraft craft, long multiplier, int depth,
        Dictionary<string, ProfitableCraft> lookup, HashSet<string> visited)
    {
        if (craft.Ingredients == null)
            return;
        foreach (var ingredient in craft.Ingredients)
        {
            var neededCount = ingredient.Count * multiplier;
            var wasCrafted = ingredient.Type == "craft";
            var canExpand = wasCrafted && depth + 1 < MaxTreeDepth
                && lookup.TryGetValue(ingredient.ItemId, out var sub) && sub.Ingredients != null
                && !visited.Contains(ingredient.ItemId);
            nodes.Add(new CraftNode
            {
                Tag = ingredient.ItemId,
                Count = neededCount,
                Cost = ingredient.Cost * multiplier,
                Method = ingredient.Type ?? "buy",
                Depth = depth,
                Expanded = canExpand
            });
            if (canExpand)
            {
                var nextVisited = new HashSet<string>(visited) { ingredient.ItemId };
                AddIngredients(nodes, lookup[ingredient.ItemId], neededCount, depth + 1, lookup, nextVisited);
            }
        }
    }

    private static void RenderCraftTree(MinecraftSocket socket, DialogBuilder db, CraftTree tree)
    {
        if (tree == null || tree.Nodes.Count == 0)
            return;
        var subCraftCount = tree.Nodes.Count(n => n.Method == "craft");
        db.LineBreak()
            .MsgLine($"{McColorCodes.YELLOW}Craft recipe breakdown{McColorCodes.GRAY} ({(subCraftCount > 0 ? $"{McColorCodes.GREEN}{subCraftCount} sub-craft(s) used" : "no sub-crafts, all bought directly")}{McColorCodes.GRAY}):", null,
                $"{McColorCodes.GRAY}Shows the cheapest path found for each ingredient.\n"
                + $"{McColorCodes.GREEN}crafted{McColorCodes.GRAY} = building it was cheaper than buying it\n"
                + $"{McColorCodes.GRAY}bought = bought directly, {McColorCodes.AQUA}npc{McColorCodes.GRAY} = bought from an npc shop");
        db.ForEach(tree.Nodes, (db, node) => db.MsgLine(FormatNode(socket, tree, node), NodeClick(tree, node), NodeHover(socket, node)));
        db.MsgLine($"Cheapest craft cost: {McColorCodes.GOLD}{socket.FormatPrice(tree.Root.CraftCost)} coins{McColorCodes.GRAY} (using the sub-crafts marked above)");
    }

    private static string FormatNode(MinecraftSocket socket, CraftTree tree, CraftNode node)
    {
        var indent = string.Concat(Enumerable.Repeat($"{McColorCodes.DARK_GRAY}  ", node.Depth));
        var branch = node.Depth > 0 ? $"{McColorCodes.DARK_GRAY}└ " : " ";
        var info = tree.Names.GetValueOrDefault(node.Tag, new ItemInfo(node.Tag, McColorCodes.WHITE, false));
        var (methodColor, methodText) = node.Method switch
        {
            "craft" => (McColorCodes.GREEN, node.Expanded ? "crafted" : "crafted*"),
            "npc" => (McColorCodes.AQUA, "npc"),
            _ => (McColorCodes.GRAY, "bought")
        };
        return $"{indent}{branch}{info.Color}{info.Name} {McColorCodes.GRAY}x{node.Count} "
            + $"{methodColor}{methodText} {McColorCodes.GRAY}~{McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}";
    }

    private static string NodeClick(CraftTree tree, CraftNode node)
    {
        if (node.Method == "craft")
            return $"/cofl recipe {node.Tag}";
        var info = tree.Names.GetValueOrDefault(node.Tag);
        if (info == null)
            return null;
        return info.IsBazaar ? $"/cofl bazaar {info.Name}" : $"/cofl ahs {info.Name}";
    }

    private static string NodeHover(MinecraftSocket socket, CraftNode node)
    {
        return node.Method switch
        {
            "craft" => $"{McColorCodes.GRAY}Sub-crafted because that was cheaper than buying it.\n"
                + (node.Expanded ? $"{McColorCodes.YELLOW}Its ingredients are listed below." : $"{McColorCodes.YELLOW}Click to view its recipe."),
            "npc" => $"{McColorCodes.GRAY}Bought from an npc shop for {McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}",
            _ => $"{McColorCodes.GRAY}Bought directly for {McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}{McColorCodes.GRAY}, click to open the market"
        };
    }
}
