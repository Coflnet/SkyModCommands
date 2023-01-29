using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public abstract class ReadOnlyListCommand<T> : McCommand
{
    protected Dictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>> sorters = new Dictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>>();
    public override async Task Execute(MinecraftSocket socket, string args)
    {
        var arguments = JsonConvert.DeserializeObject<string>(args);
        var elements = (await GetElements(socket, arguments)).ToList();
        if (sorters.ContainsKey(arguments.Split(' ')[0].Trim('"')))
        {
            elements = sorters[arguments.Trim('"')](elements).ToList();
            arguments = RemoveSortArgument(arguments);
        }
        if (!int.TryParse(arguments.Trim('"'), out int page) && arguments.Trim('"').Length > 1)
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
            .ForEach(toDisplay, (db, elem) => Format(socket, db, elem));
        if(toDisplay.Count() == 0)
            dialog.MsgLine("No elements found");
        PrintSumary(socket, dialog, elements);
        socket.SendMessage(dialog.Build());
    }

    private static string RemoveSortArgument(string arguments)
    {
        arguments = arguments.Split(' ').Skip(1).Aggregate((a, b) => a + " " + b);
        return arguments;
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
