using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public abstract class ReadOnlyListCommand<T> : McCommand
{
    public override bool IsPublic => true;
    protected Dictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>> sorters = new Dictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>>();
    public override async Task Execute(MinecraftSocket socket, string args)
    {
        var arguments = JsonConvert.DeserializeObject<string>(args);
        var title = GetTitle(arguments);
        if (arguments.ToLower() == "help")
        {
            socket.Dialog(db => db.MsgLine(Title).MsgLine($"Usage: {McColorCodes.AQUA}/cofl {Slug} {McColorCodes.GOLD}[sort] {McColorCodes.AQUA}[search|page] ")
                .If(() => sorters.Count > 0, db => db.MsgLine($"Sort options: {McColorCodes.GOLD}" + string.Join($"{McColorCodes.GRAY}, {McColorCodes.GOLD}", sorters.Keys))));
            return;
        }
        var elements = (await GetElements(socket, arguments)).ToList();
        if (sorters.TryGetValue(arguments.Split(' ')[0], out var sorter))
        {
            elements = sorter(elements).ToList();
            arguments = RemoveSortArgument(arguments);
        }
        elements = FilterElementsForProfile(socket, elements).ToList();
        if (!int.TryParse(arguments, out int page) && arguments.Length > 1)
        {
            // search
            elements = elements.Where(e => GetId(e).ToLower().Contains(arguments.ToLower())).ToList();
        }
        if (page < 0)
            page = elements.Count / PageSize + page;
        if (page == 0)
            page = 1;
        var toDisplay = elements.Skip((page - 1) * PageSize).Take(PageSize);
        var totalPages = elements.Count / PageSize + 1;
        DialogBuilder dialog = PrintResult(socket, title, page, toDisplay, totalPages);
        if (toDisplay.Count() == 0)
            dialog.MsgLine(NoMatchText);
        PrintSumary(socket, dialog, elements, toDisplay);
        socket.SendMessage(dialog.Build());
    }

    protected virtual DialogBuilder PrintResult(MinecraftSocket socket, string title, int page, IEnumerable<T> toDisplay, int totalPages)
    {
        return DialogBuilder.New.MsgLine($"{title} (page {page}/{totalPages})", $"/cofl {Slug} {page+1}", $"Click to go to next page ({page+1})")
                    .ForEach(toDisplay, (db, elem) => Format(socket, db, elem));
    }

    protected virtual string NoMatchText => "No elements found";

    protected virtual string RemoveSortArgument(string arguments)
    {
        if (arguments.Split(' ').Length == 1)
            return "";
        arguments = arguments.Split(' ').Skip(1).Aggregate((a, b) => a + " " + b);
        return arguments;
    }

    protected virtual void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<T> elements, IEnumerable<T> toDisplay)
    {
    }

    protected virtual IEnumerable<T> FilterElementsForProfile(MinecraftSocket socket, IEnumerable<T> elements)
    {
        return elements;
    }
    protected virtual string GetTitle(string arguments)
    {
        return Title;
    }
    protected abstract Task<IEnumerable<T>> GetElements(MinecraftSocket socket, string val);
    protected abstract void Format(MinecraftSocket socket, DialogBuilder db, T elem);
    protected abstract string GetId(T elem);
    protected virtual string Title => "Elements";
    protected virtual int PageSize => 14;
}
