using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
namespace Coflnet.Sky.Commands.MC;

public class BzMoveCommand : ReadOnlyListCommand<BzMoveCommand.MovementElement>
{
    public override bool IsPublic => true;
    protected override string Title => "Top Bazaar 24h Movers";
    protected override string NoMatchText => $"No match found, that should not be possible, guess there is a bug";
    public BzMoveCommand()
    {
        sorters.Add("asc", (el) => el.OrderBy(m =>
            m.Movement.PreviousPrice != 0
            ? (m.Movement.CurrentPrice - m.Movement.PreviousPrice) / m.Movement.PreviousPrice
            : 0));
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, MovementElement elem)
    {
        db.Msg($" {elem.ItemName} {McColorCodes.RED}{socket.FormatPrice(elem.Movement.PreviousPrice)} {McColorCodes.GRAY}-> {McColorCodes.GREEN}{socket.FormatPrice(elem.Movement.CurrentPrice)}", "/bz " + elem.ItemName, $"open {McColorCodes.AQUA}{elem.ItemName} {McColorCodes.GRAY}in game")
            .Button($"Website", $"https://sky.coflnet.com/item/{elem.Movement.ItemId}", $"open {McColorCodes.AQUA}{elem.ItemName} {McColorCodes.GRAY}in browser")
            .LineBreak();
    }

    protected override async Task<IEnumerable<MovementElement>> GetElements(MinecraftSocket socket, string val)
    {
        var items = socket.GetService<IBazaarApi>();
        var movementTask = items.GetMovementAsync(24, val.Contains("buy"));
        var itemsApi = socket.GetService<Items.Client.Api.IItemsApi>();

        var names = (await itemsApi.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
        return (await movementTask)
            .Select(m => new MovementElement
            {
                Movement = m,
                ItemName = names.TryGetValue(m.ItemId, out var name) ? name : m.ItemId
            })
            .OrderByDescending(m =>
            m.Movement.PreviousPrice != 0
                ? (m.Movement.CurrentPrice - m.Movement.PreviousPrice) / m.Movement.PreviousPrice
                : 0)
            .ToList();
    }


    protected override string RemoveSortArgument(string arguments)
    {
        var args = arguments.Split(' ').Where(a =>
        {
            if (sorters.ContainsKey(a))
                return false;
            if (a == "buy" || a == "sell")
                return false; // special selection filter
            return true;
        }).ToList();

        if (args.Count == 0)
            return "";

        arguments = args.Aggregate((a, b) => a + " " + b);

        return arguments;
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<MovementElement> elements, IEnumerable<MovementElement> toDisplay)
    {
        var hidden = toDisplay.Count(e => e.Movement.PreviousPrice == 0);
        var isDescending = elements.FirstOrDefault()?.Movement.CurrentPrice - elements.FirstOrDefault()?.Movement.PreviousPrice > 0;
        db.If(() => isDescending, db => db.Button("Drop", "/cofl bzmove asc", $"Sort by biggest drop first\n{McColorCodes.GRAY}You can also use {McColorCodes.AQUA}/cl bzmove asc <search>\n{McColorCodes.GRAY} to search the results"),
            db => db.Button("Upwards", "/cofl bzmove", "sort by biggest increase first"))
            .If(() => hidden > 0, db => db.Msg($" Hid {hidden}", null, "Elements that had no previous\norders are hidden"));
    }

    protected override string GetId(MovementElement elem)
    {
        return elem.ItemName + elem.Movement.ItemId;
    }

    public class MovementElement
    {
        public Bazaar.Client.Model.ItemPriceMovement Movement { get; set; }
        public string ItemName { get; set; }
    }
}
