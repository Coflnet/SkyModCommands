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
    protected override string Title => "Top Bazaar Movers";
    protected override string NoMatchText => $"No match found, that should not be possible, guess there is a bug";

    protected override void Format(MinecraftSocket socket, DialogBuilder db, MovementElement elem)
    {
        db.MsgLine($"");
        sorters.Add("asc", (el) => el.OrderBy(m => m.Movement.CurrentPrice - m.Movement.PreviousPrice));
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
            .Where(m => m.ItemName.Contains(val, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Movement.CurrentPrice - m.Movement.PreviousPrice)
            .ToList();
    }

    protected override string GetId(MovementElement elem)
    {
        throw new NotImplementedException();
    }

    public class MovementElement
    {
        public Bazaar.Client.Model.ItemPriceMovement Movement { get; set; }
        public string ItemName { get; set; }
    }
}
