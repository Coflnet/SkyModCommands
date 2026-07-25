using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Microsoft.Extensions.Configuration;
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
        public bool FullSubcraftAvailable;
        public long CraftedCount;
        public bool Enough;
        public AcquisitionPlan? Acquisition;
        /// <summary>How this ingredient is obtained: "craft", "npc" or "buy".</summary>
        public string Method;
        public int Depth;
        /// <summary>True when this node was sub-crafted and its ingredients are listed below it.</summary>
        public bool Expanded;
    }

    private class CraftTree
    {
        public double Cost;
        public List<CraftNode> Nodes;
        public Dictionary<string, ItemInfo> Names;
    }

    private record ItemInfo(string Name, string Color, bool IsBazaar);

    internal class BackendAcquisitionFill
    {
        public string Source { get; set; }
        public long Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double Cost { get; set; }
    }

    internal record AcquisitionBucket(long Qty, double UnitPrice, double Cost);

    internal record AcquisitionPlan(
        AcquisitionBucket Npc,
        AcquisitionBucket Order,
        AcquisitionBucket Insta,
        long Unmet,
        long TotalCount,
        double TotalCost);

    internal class BackendAcquisitionPlan
    {
        public string ItemId { get; set; }
        public long Quantity { get; set; }
        public double Cost { get; set; }
        public bool Enough { get; set; }
        public string Method { get; set; }
        public double DirectBuyCost { get; set; }
        public bool DirectBuyEnough { get; set; }
        public double CraftCost { get; set; }
        public bool CraftEnough { get; set; }
        public long CraftedQuantity { get; set; }
        public List<BackendAcquisitionFill> Purchases { get; set; } = new();
        public List<BackendAcquisitionPlan> Ingredients { get; set; } = new();
    }

    /// <summary>
    /// Fetches the quantity-specific acquisition tree selected by SkyCrafts and flattens it for chat.
    /// All pricing, order-book walking, and buy-vs-craft decisions stay in SkyCrafts.
    /// </summary>
    private static async Task<CraftTree> BuildCraftTree(MinecraftSocket socket, string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        try
        {
            var itemsTask = socket.GetService<IItemsApi>().ItemsGetAsync();
            var baseUrl = socket.GetService<IConfiguration>()["CRAFTS_BASE_URL"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            var url = $"{baseUrl}/crafts/acquisition/{Uri.EscapeDataString(tag)}?quantity=1&forceCraft=true";
            var json = await socket.GetService<HttpClient>().GetStringAsync(url);
            var root = JsonConvert.DeserializeObject<BackendAcquisitionPlan>(json);
            if (root?.Ingredients == null || root.Ingredients.Count == 0)
                return null;

            var items = await itemsTask;
            var names = items
                .GroupBy(i => i.Tag).Select(g => g.First())
                .ToDictionary(i => i.Tag, i => new ItemInfo(
                    i.Name ?? i.Tag,
                    socket.formatProvider.GetRarityColor((Core.Tier)i.Tier),
                    i.Flags.HasValue && i.Flags.Value.HasFlag(Items.Client.Model.ItemFlags.BAZAAR)));

            var nodes = new List<CraftNode>();
            foreach (var ingredient in root.Ingredients)
                AddPlan(nodes, ingredient, 0);
            return new CraftTree { Cost = root.Ingredients.Sum(i => i.Cost), Nodes = nodes, Names = names };
        }
        catch (Exception e)
        {
            socket.Error(e, "building craft breakdown tree");
            return null;
        }
    }

    internal static void AddPlan(List<CraftNode> nodes, BackendAcquisitionPlan plan, int depth)
    {
        var purchases = plan.Purchases ?? new List<BackendAcquisitionFill>();
        var acquisition = purchases.Count == 0 ? null : new AcquisitionPlan(
            Bucket(purchases, "npc"),
            Bucket(purchases, "order"),
            Bucket(purchases, "insta"),
            plan.Enough ? 0 : Math.Max(0, plan.Quantity - purchases.Sum(p => p.Quantity)),
            plan.Quantity,
            purchases.Sum(p => p.Cost));
        var expanded = plan.CraftedQuantity > 0 && plan.Ingredients?.Count > 0 && depth + 1 < MaxTreeDepth;
        nodes.Add(new CraftNode
        {
            Tag = plan.ItemId,
            Count = plan.Quantity,
            Cost = plan.Cost,
            DirectBuyCost = plan.DirectBuyCost,
            DirectBuyAvailable = plan.DirectBuyEnough,
            FullSubcraftCost = plan.CraftCost,
            FullSubcraftAvailable = plan.CraftEnough,
            CraftedCount = plan.CraftedQuantity,
            Enough = plan.Enough,
            Acquisition = acquisition,
            Method = plan.Method,
            Depth = depth,
            Expanded = expanded
        });
        if (expanded)
            foreach (var ingredient in plan.Ingredients)
                AddPlan(nodes, ingredient, depth + 1);
    }

    private static AcquisitionBucket Bucket(IEnumerable<BackendAcquisitionFill> purchases, string source)
    {
        var selected = purchases.Where(p => p.Source == source).ToList();
        var quantity = selected.Sum(p => p.Quantity);
        var cost = selected.Sum(p => p.Cost);
        return new AcquisitionBucket(quantity, quantity > 0 ? cost / quantity : 0, cost);
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
        db.MsgLine($"Cheapest craft cost: {McColorCodes.GOLD}{socket.FormatPrice(tree.Cost)} coins{McColorCodes.GRAY} (using the sub-crafts marked above)");
    }

    private static string FormatNode(MinecraftSocket socket, CraftTree tree, CraftNode node)
    {
        var indent = string.Concat(Enumerable.Repeat($"{McColorCodes.DARK_GRAY}  ", node.Depth));
        var branch = node.Depth > 0 ? $"{McColorCodes.DARK_GRAY}└ " : " ";
        var info = tree.Names.GetValueOrDefault(node.Tag, new ItemInfo(node.Tag, McColorCodes.WHITE, false));
        var methodText = GetNodeMethodText(node);
        var methodColor = !node.Enough ? McColorCodes.RED : node.Method switch
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
        if (!node.Enough)
            return "insufficient supply";
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
                return (node.FullSubcraftAvailable
                        ? $"{McColorCodes.GRAY}Craft all from the ingredients below: {McColorCodes.GOLD}{socket.FormatPrice(node.FullSubcraftCost)}\n"
                        : $"{McColorCodes.GRAY}Craft all: {McColorCodes.YELLOW}not enough known ingredient supply\n")
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
            lines.Add($"{McColorCodes.GRAY}Insta buy{McColorCodes.GRAY} x{plan.Insta.Qty} @ {McColorCodes.GOLD}{socket.FormatPrice(plan.Insta.UnitPrice)} average");

        if (node.CraftedCount > 0)
            lines.Add($"{McColorCodes.GREEN}Craft{McColorCodes.GRAY} x{node.CraftedCount} from listed ingredients for "
                + $"{McColorCodes.GOLD}{socket.FormatPrice(node.Cost - plan.TotalCost)}");
        if (lines.Count == 0)
            lines.Add("No market tranches were available to split this ingredient.");
        if (plan.Unmet > 0 && node.CraftedCount == 0)
            lines.Add($"\n{McColorCodes.YELLOW}{plan.Unmet} units cannot be sourced from known channels");

        if (node.FullSubcraftCost > 0)
        {
            lines.Add(node.FullSubcraftAvailable
                ? $"{McColorCodes.GRAY}Craft all: {McColorCodes.GOLD}{socket.FormatPrice(node.FullSubcraftCost)}"
                : $"{McColorCodes.GRAY}Craft all: {McColorCodes.YELLOW}not enough known ingredient supply");
            lines.Add(node.DirectBuyAvailable
                ? $"{McColorCodes.GRAY}Buy all directly: {McColorCodes.GOLD}{socket.FormatPrice(node.DirectBuyCost)}"
                : $"{McColorCodes.GRAY}Buy all directly: {McColorCodes.YELLOW}not enough known supply");
        }

        return $"{McColorCodes.GRAY}Acquisition split for {node.Count}x:\n{string.Join("\n", lines)}\n"
            + $"{McColorCodes.GRAY}Chosen total: {McColorCodes.GOLD}{socket.FormatPrice(node.Cost)}{McColorCodes.GRAY}";
    }
}
