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
    internal class CraftNode
    {
        public string Tag;
        public long Count;
        public double Cost;
        public double DirectBuyCost;
        public bool DirectBuyAvailable;
        public double FullSubcraftCost;
        public long CraftedCount;
        public AcquisitionPlan? Acquisition;
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

    internal enum AcquisitionMode
    {
        Order,
        Insta
    }

    internal record AcquisitionBucket(long Qty, double UnitPrice, double Cost);

    internal record AcquisitionPlan(
        AcquisitionMode Mode,
        AcquisitionBucket Npc,
        AcquisitionBucket Order,
        AcquisitionBucket Insta,
        long Unmet,
        long TotalCount,
        double TotalCost);

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
    internal static double AddIngredients(List<CraftNode> nodes, ProfitableCraft craft, long multiplier, int depth,
        Dictionary<string, ProfitableCraft> lookup, HashSet<string> visited)
    {
        if (craft.Ingredients == null)
            return 0;
        double totalCost = 0;
        foreach (var ingredient in craft.Ingredients)
        {
            var neededCount = ingredient.Count * multiplier;
            var wasCrafted = ingredient.Type == "craft";
            lookup.TryGetValue(ingredient.ItemId, out var sub);
            var canEvaluateCraft = wasCrafted && depth + 1 < MaxTreeDepth
                && sub?.Ingredients?.Count > 0
                && !visited.Contains(ingredient.ItemId);
            var fullBuyPlan = GetAcquisitionPlan(ingredient, neededCount, AcquisitionMode.Order);
            var directBuyCost = GetDirectBuyCost(ingredient, neededCount);
            var directBuyAvailable = fullBuyPlan == null ? directBuyCost > 0 : fullBuyPlan.Unmet == 0;
            var selectedCost = directBuyCost;
            var selectedPlan = fullBuyPlan;
            var selectedChildNodes = new List<CraftNode>();
            var fullSubcraftCost = 0d;
            var craftedCount = 0L;
            if (canEvaluateCraft)
            {
                var nextVisited = new HashSet<string>(visited) { ingredient.ItemId };
                var fullChildNodes = new List<CraftNode>();
                fullSubcraftCost = AddIngredients(fullChildNodes, sub, neededCount, depth + 1, lookup, nextVisited);
                selectedCost = fullSubcraftCost;
                selectedPlan = null;
                selectedChildNodes = fullChildNodes;
                craftedCount = neededCount;

                var craftUnitCost = fullSubcraftCost / Math.Max(1L, neededCount);
                var splitPlan = GetAcquisitionPlan(ingredient, neededCount, AcquisitionMode.Order, craftUnitCost);
                if (splitPlan != null)
                {
                    var splitChildNodes = fullChildNodes;
                    var splitSubcraftCost = fullSubcraftCost;
                    if (splitPlan.Unmet == 0)
                    {
                        splitChildNodes = new List<CraftNode>();
                        splitSubcraftCost = 0;
                    }
                    else if (splitPlan.Unmet != neededCount)
                    {
                        splitChildNodes = new List<CraftNode>();
                        splitSubcraftCost = AddIngredients(splitChildNodes, sub, splitPlan.Unmet, depth + 1, lookup, nextVisited);
                    }

                    var splitCost = splitPlan.TotalCost + splitSubcraftCost;
                    if (splitCost < selectedCost)
                    {
                        selectedCost = splitCost;
                        selectedPlan = splitPlan;
                        selectedChildNodes = splitChildNodes;
                        craftedCount = splitPlan.Unmet;
                    }
                }

                if (directBuyAvailable && directBuyCost <= selectedCost)
                {
                    selectedCost = directBuyCost;
                    selectedPlan = fullBuyPlan;
                    selectedChildNodes.Clear();
                    craftedCount = 0;
                }
            }
            nodes.Add(new CraftNode
            {
                Tag = ingredient.ItemId,
                Count = neededCount,
                Cost = selectedCost,
                DirectBuyCost = directBuyCost,
                DirectBuyAvailable = directBuyAvailable,
                FullSubcraftCost = fullSubcraftCost,
                CraftedCount = craftedCount,
                Acquisition = selectedPlan,
                Method = craftedCount > 0 ? "craft" : selectedPlan?.Unmet == 0 && selectedPlan.Npc.Qty == neededCount ? "npc" : "buy",
                Depth = depth,
                Expanded = craftedCount > 0 && selectedChildNodes.Count > 0
            });
            nodes.AddRange(selectedChildNodes);
            totalCost += selectedCost;
        }
        return totalCost;
    }

    internal static double GetDirectBuyCost(Ingredient ingredient, long totalCount)
    {
        var plan = GetAcquisitionPlan(ingredient, totalCount, AcquisitionMode.Order);
        if (plan != null && plan.Unmet == 0)
            return plan.TotalCost;
        var fallbackCost = ingredient.BuyOrderCost > 0 ? ingredient.BuyOrderCost : ingredient.Cost;
        return fallbackCost * totalCount / Math.Max(1d, ingredient.Count);
    }

    private static AcquisitionPlan? GetAcquisitionPlan(Ingredient ingredient, long totalCount,
        AcquisitionMode mode = AcquisitionMode.Order, double maxUnitPrice = double.PositiveInfinity)
    {
        var npcCap = Math.Max(0L, ingredient.NpcCapacity);
        var npcUnit = ingredient.NpcUnitPrice;
        var orderCap = Math.Max(0L, ingredient.BuyOrderCapacity);
        var marketPrices = new List<double>();

        if (ingredient.BuyOrderUnitPrice > 0)
            marketPrices.Add(ingredient.BuyOrderUnitPrice);
        if (ingredient.InstaBuyUnitPrice > 0)
            marketPrices.Add(ingredient.InstaBuyUnitPrice);

        var orderUnit = orderCap > 0 && marketPrices.Count > 0 ? marketPrices.Min() : 0d;
        var instaUnit = marketPrices.Count > 0 ? marketPrices.Max() : 0d;
        if ((npcCap <= 0 || npcUnit <= 0) && (orderCap <= 0 || orderUnit <= 0) && instaUnit <= 0)
            return null;

        var total = Math.Max(0L, totalCount);
        var remaining = total;

        var npcQty = npcUnit > 0 && npcUnit < maxUnitPrice ? Math.Min(remaining, npcCap) : 0;
        remaining -= npcQty;

        var useBuyOrders = mode == AcquisitionMode.Order;
        var orderQty = useBuyOrders && orderUnit > 0 && orderUnit < maxUnitPrice ? Math.Min(remaining, orderCap) : 0;
        remaining -= orderQty;

        var instaQty = instaUnit > 0 && instaUnit < maxUnitPrice ? remaining : 0;
        remaining -= instaQty;

        var npc = new AcquisitionBucket(npcQty, npcUnit, npcQty * npcUnit);
        var order = new AcquisitionBucket(orderQty, orderUnit, orderQty * orderUnit);
        var insta = new AcquisitionBucket(instaQty, instaUnit, instaQty * instaUnit);

        return new AcquisitionPlan(mode, npc, order, insta, remaining, total, npc.Cost + order.Cost + insta.Cost);
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
                + $"{McColorCodes.GRAY}bought = market split (npc -> buy order -> insta)");
        db.ForEach(tree.Nodes, (db, node) => db.MsgLine(FormatNode(socket, tree, node), NodeClick(tree, node), NodeHover(socket, node)));
        db.MsgLine($"Cheapest craft cost: {McColorCodes.GOLD}{socket.FormatPrice(tree.Root.CraftCost)} coins{McColorCodes.GRAY} (using the sub-crafts marked above)");
    }

    private static string FormatNode(MinecraftSocket socket, CraftTree tree, CraftNode node)
    {
        var indent = string.Concat(Enumerable.Repeat($"{McColorCodes.DARK_GRAY}  ", node.Depth));
        var branch = node.Depth > 0 ? $"{McColorCodes.DARK_GRAY}└ " : " ";
        var info = tree.Names.GetValueOrDefault(node.Tag, new ItemInfo(node.Tag, McColorCodes.WHITE, false));
        var methodText = GetNodeMethodText(node);
        var methodColor = node.Method switch
        {
            "craft" => McColorCodes.GREEN,
            "npc" => node.Acquisition == null || (node.Acquisition.Order.Qty == 0 && node.Acquisition.Insta.Qty == 0)
                ? McColorCodes.AQUA
                : McColorCodes.GRAY,
            _ => McColorCodes.GRAY
        };
        return $"{indent}{branch}{info.Color}{info.Name} {McColorCodes.GRAY}x{node.Count} "
            + $"{methodColor}{methodText} {McColorCodes.GRAY}~{McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}";
    }

    private static string GetNodeMethodText(CraftNode node)
    {
        var channels = new List<string>();
        if (node.Acquisition?.Npc.Qty > 0)
            channels.Add("npc");
        if (node.Acquisition?.Order.Qty > 0)
            channels.Add("order");
        if (node.Acquisition?.Insta.Qty > 0)
            channels.Add("insta");
        if (node.CraftedCount > 0)
            channels.Add(node.Expanded ? "crafted" : "crafted*");

        return channels.Count == 0 ? node.Method == "npc" ? "npc" : "bought" : string.Join("/", channels);
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
        if (node.Acquisition == null)
        {
            if (node.FullSubcraftCost > 0)
                return $"{McColorCodes.GRAY}Craft all from the ingredients below: {McColorCodes.GOLD}{socket.FormatPrice(node.FullSubcraftCost)}\n"
                    + $"{McColorCodes.GRAY}Buy all directly: {McColorCodes.GOLD}{socket.FormatPrice(node.DirectBuyCost)}\n"
                    + $"{McColorCodes.GRAY}Chosen total: {McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}";
            return $"{McColorCodes.GRAY}Bought directly for {McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}{McColorCodes.GRAY}, click to open the market";
        }

        var plan = node.Acquisition;
        var lines = new List<string>();
        if (plan.Npc.Qty > 0)
            lines.Add($"{McColorCodes.AQUA}NPC{McColorCodes.GRAY} x{plan.Npc.Qty} @ {McColorCodes.GOLD}{socket.FormatPrice(plan.Npc.UnitPrice)} each");
        if (plan.Order.Qty > 0)
            lines.Add($"{McColorCodes.GRAY}Buy order{McColorCodes.GRAY} x{plan.Order.Qty} @ {McColorCodes.GOLD}{socket.FormatPrice(plan.Order.UnitPrice)} each");
        if (plan.Insta.Qty > 0)
            lines.Add($"{McColorCodes.GRAY}Insta buy{McColorCodes.GRAY} x{plan.Insta.Qty} @ {McColorCodes.GOLD}{socket.FormatPrice(plan.Insta.UnitPrice)} each");

        if (node.CraftedCount > 0)
            lines.Add($"{McColorCodes.GREEN}Craft{McColorCodes.GRAY} x{node.CraftedCount} from listed ingredients for "
                + $"{McColorCodes.GOLD}{socket.FormatPrice(node.Cost - plan.TotalCost)}");
        if (lines.Count == 0)
            lines.Add("No market tranches were available to split this ingredient.");
        if (plan.Unmet > 0 && node.CraftedCount == 0)
            lines.Add($"\n{McColorCodes.YELLOW}{plan.Unmet} units cannot be sourced from known channels");

        if (node.FullSubcraftCost > 0)
        {
            lines.Add($"{McColorCodes.GRAY}Craft all: {McColorCodes.GOLD}{socket.FormatPrice(node.FullSubcraftCost)}");
            lines.Add(node.DirectBuyAvailable
                ? $"{McColorCodes.GRAY}Buy all directly: {McColorCodes.GOLD}{socket.FormatPrice(node.DirectBuyCost)}"
                : $"{McColorCodes.GRAY}Buy all directly: {McColorCodes.YELLOW}not enough known supply");
        }

        return $"{McColorCodes.GRAY}Acquisition split for {node.Count}x:\n{string.Join("\n", lines)}\n"
            + $"{McColorCodes.GRAY}Chosen total: {McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}{McColorCodes.GRAY}";
    }
}
