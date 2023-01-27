using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public abstract class ReadOnlyListCommand<T> : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var elements = (await GetElements(socket, arguments.Trim('"'))).ToList();
        if(!int.TryParse(arguments.Trim('"'), out int page) && arguments.Trim('"').Length > 1)
        {
            // search
            elements = elements.Where(e => GetId(e).ToLower().Contains(arguments.Trim('"').ToLower())).ToList();
        }
        if (page < 0)
            page = elements.Count / 10 + page;
        if (page == 0)
            page = 1;
        var toDisplay = elements.Skip((page - 1) * PageSize).Take(PageSize);
        var totalPages = elements.Count / PageSize + 1;
        var dialog = DialogBuilder.New.MsgLine($"{Title} (page {page}/{totalPages})")
            .ForEach(toDisplay, (db,elem)=>Format(socket,db,elem));
        PrintSumary(socket, dialog, elements);
        socket.SendMessage(dialog.Build());
    }
    protected virtual void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<T> elements)
    {
    }
    protected abstract Task<IEnumerable<T>> GetElements(MinecraftSocket socket, string val);
    protected abstract void Format(MinecraftSocket socket, DialogBuilder db, T elem);
    protected abstract string GetId(T elem);
    protected virtual string Title => "Elements";
    protected virtual int PageSize => 14;
}
