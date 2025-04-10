using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public abstract class ListCommand<TElem, TCol> : McCommand where TCol : ICollection<TElem>
    {
        public override bool IsPublic => true;
        protected virtual bool CanAddMultiple { get; } = true;
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string>(arguments).Replace(@"\u003d", "=");
            var subArgStart = args.IndexOf(' ');
            var subArgs = args.Substring(subArgStart + 1);
            if (subArgStart == -1)
                subArgs = "";
            switch (args.Split(' ').First())
            {
                case "list":
                case "l":
                case "ls":
                    await List(socket, subArgs);
                    break;
                case "remove":
                case "delete":
                case "rm":
                    await Remove(socket, subArgs);
                    break;
                case "help":
                case "h":
                    await Help(socket, subArgs);
                    break;
                case "add":
                case "a":
                    await Add(socket, subArgs);
                    break;
                case "addall":
                    await AddAll(socket, subArgs);
                    break;
                case "e":
                case "edit":
                    await Edit(socket, subArgs);
                    break;
                default:
                    await DefaultAction(socket, args);
                    break;
            }
        }

        protected virtual async Task Edit(MinecraftSocket socket, string subArgs)
        {
            var targetElem = await Find(socket, subArgs.Split('|').FirstOrDefault());
            if (targetElem.Count == 0)
            {
                socket.SendMessage(new DialogBuilder().MsgLine($"Could not find {McColorCodes.AQUA}{subArgs}{DEFAULT_COLOR} in list"));
                return;
            }
            if (targetElem.Count > 1)
            {
                socket.SendMessage(new DialogBuilder()
                    .MsgLine($"To many matches for {McColorCodes.AQUA}{subArgs}{DEFAULT_COLOR} please select")
                    .ForEach(targetElem, (d, o) => d.MsgLine(
                        $"{Format(o)} {McColorCodes.YELLOW}[REMOVE]",
                        $"/cofl {Slug} rm {GetId(o)}",
                        $"Remove {LongFormat(o)}")));
            }

            var list = await GetList(socket);
            var elem = targetElem.First();
            list.Remove(elem);
            var editArgs = subArgs.Split('|').Skip(1).FirstOrDefault();
            await UpdateElem(socket, targetElem.First(), editArgs);
            socket.SendMessage(new DialogBuilder().MsgLine($"Removed {Format(elem)}"));

        }

        protected virtual Task<TElem> UpdateElem(MinecraftSocket socket, TElem current, string args)
        {
            return Task.FromResult(current);
        }

        protected virtual Task DefaultAction(MinecraftSocket socket, string args)
        {
            return Help(socket, args);
        }

        protected virtual async Task Add(MinecraftSocket socket, string subArgs)
        {
            if (subArgs.StartsWith("!json"))
            {
                var newEntry = JsonConvert.DeserializeObject<TElem>(subArgs.Substring(5));
                await AddEntry(socket, newEntry);
                return;
            }
            var options = await CreateFrom(socket, subArgs);
            if (options.Count() == 0)
            {
                socket.SendMessage(new DialogBuilder()
                .MsgLine($"Could not create a new entry, please check your input and try something else"));
                return;
            }
            if (options.Count() == 1)
            {
                var newEntry = options.First().Element;
                await AddEntry(socket, newEntry);
                return;
            }
            socket.SendMessage(new DialogBuilder()
                .MsgLine($"Could not create a new entry, to many possible matches, please select one:")
                .ForEach(options, (d, o) => d.MsgLine($"{Format(o.Element)} {McColorCodes.YELLOW}[ADD]", $"/cofl {Slug} add !json{JsonConvert.SerializeObject(o.Element)}", $"Add {LongFormat(o.Element)}"))
                .If(() => CanAddMultiple, db => db.MsgLine($"{McColorCodes.YELLOW}[ADD ALL]", $"/cofl {Slug} addall {subArgs}", $"Add all the above")));
        }

        protected virtual async Task AddAll(MinecraftSocket socket, string subArgs)
        {
            var options = await CreateFrom(socket, subArgs);
            if (options.Count() == 0)
            {
                socket.SendMessage(new DialogBuilder()
                .MsgLine($"Could not create a new entry, please check your input and try something else"));
                return;
            }
            foreach (var item in options)
            {
                await AddEntry(socket, item.Element);
            }
        }

        protected virtual async Task AddEntry(MinecraftSocket socket, TElem newEntry)
        {
            var list = await GetList(socket);
            if ((await Find(socket, GetId(newEntry))).Count > 0)
            {
                socket.SendMessage(new DialogBuilder()
                .MsgLine($"{Format(newEntry)}{DEFAULT_COLOR} was already added, not adding again"));
                return;
            }
            list.Add(newEntry);
            await Update(socket, list);
            socket.SendMessage(new DialogBuilder()
            .MsgLine($"Added {Format(newEntry)}"));
        }

        protected virtual Task Help(MinecraftSocket socket, string subArgs)
        {
            socket.SendMessage(new DialogBuilder()
                .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} add{DEFAULT_COLOR} adds a new entry")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} rm{DEFAULT_COLOR} removes an entry")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists all entries")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} help{DEFAULT_COLOR} display this help"));
            return Task.CompletedTask;
        }

        protected virtual async Task Remove(MinecraftSocket socket, string arguments)
        {
            if (arguments == "*")
            {
                var fullList = await GetList(socket);
                fullList.Clear();
                socket.SendMessage(new DialogBuilder().MsgLine($"Removed all entries"));
                await Update(socket, fullList);
                return;
            }
            var toRemove = await Find(socket, arguments);
            if (toRemove.Count == 0) // if no exact match is find try searching
                toRemove = await Search(socket, arguments);
            if (toRemove.Count == 0)
            {
                socket.SendMessage(new DialogBuilder().MsgLine($"Could not find {McColorCodes.AQUA}{arguments}{DEFAULT_COLOR} in list"));
                return;
            }
            var list = await GetList(socket);
            if (toRemove.Count == 1 || toRemove.All(t => GetId(t) == GetId(toRemove.First())))
            {
                foreach (var item in toRemove)
                {
                    list.Remove(item);
                }
                await Update(socket, list);
                socket.SendMessage(new DialogBuilder().MsgLine($"Removed {Format(toRemove.First())}"));
            }
            else
            {
                socket.SendMessage(new DialogBuilder()
                    .MsgLine($"To many matches for {McColorCodes.AQUA}{arguments}{DEFAULT_COLOR} please select")
                    .ForEach(toRemove, (d, o) => d.MsgLine(
                        $"{Format(o)} {McColorCodes.YELLOW}[REMOVE]",
                        $"/cofl {Slug} rm {GetId(o)}",
                        $"Remove {LongFormat(o)}")));
            }

        }

        protected virtual async Task List(MinecraftSocket socket, string subArgs)
        {
            var list = await GetList(socket);
            var pageSize = 12;
            if (!int.TryParse(subArgs, out int page) && !string.IsNullOrWhiteSpace(subArgs) || page > list.Count / pageSize)
            {
                // is search value 
                socket.Dialog(db => db.MsgLine($"Search for {McColorCodes.AQUA}{subArgs}{DEFAULT_COLOR} resulted in:").
                    ForEach(list.Where(e => (LongFormat(e) + GetId(e) + Format(e)).ToLower().Contains(subArgs.ToLower())), (d, e) => ListResponse(d, e)));
                return;
            }

            if (list.Count == 0)
            {
                await NoEntriesFound(socket, subArgs);
                return;
            }

            var totalPages = list.Count / pageSize;
            var displayPage = page;
            if (displayPage == 0)
                displayPage = 1;
            else 
                page = displayPage - 1;
            if (totalPages < page)
            {
                socket.SendMessage(new DialogBuilder()
                .MsgLine($"There are only {McColorCodes.YELLOW}{totalPages + 1}{McColorCodes.WHITE} pages in total (starting from 1)", null, $"Try running it without or a smaller number"));
                return;
            }

            socket.Dialog(db => db
                .MsgLine($"Content (page {displayPage}):", $"/cofl {Slug} ls {displayPage + 1}", $"This is page {displayPage} \nthere are {totalPages} pages\nclick this to show the next page")
                .ForEach(list.Skip(page * pageSize).Take(pageSize), (d, e) =>
                {
                    ListResponse(d, e);
                }));
        }

        protected virtual Task NoEntriesFound(MinecraftSocket socket, string subArgs)
        {
            socket.SendMessage(new DialogBuilder().MsgLine($"No entries found for. You can add new ones with {McColorCodes.AQUA}/cofl {Slug} add <argument>{DEFAULT_COLOR}"));
            return Task.CompletedTask;
        }

        protected virtual void ListResponse(DialogBuilder d, TElem e)
        {
            FormatForList(d, e).MsgLine($" {McColorCodes.YELLOW}[REMOVE]{DEFAULT_COLOR}", $"/cofl {Slug} rm {GetId(e)}", $"remove {LongFormat(e)}");
        }

        protected virtual DialogBuilder FormatForList(DialogBuilder d, TElem e)
        {
            var formatted = Format(e);
            return d.Msg(formatted);
        }

        protected virtual async Task<ICollection<TElem>> Find(MinecraftSocket socket, string val)
        {
            var list = await GetList(socket);
            return list.Where(l => GetId(l) == val).ToList();
        }

        protected virtual async Task<ICollection<TElem>> Search(MinecraftSocket socket, string val)
        {
            var list = await GetList(socket);
            return list.Where(l => (GetId(l) + LongFormat(l)).ToLower().Contains(val.ToLower())).ToList();
        }

        /// <summary>
        /// Try to create a new entry based on given parameters
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="val"></param>
        /// <returns></returns>
        protected abstract Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val);
        protected abstract Task<TCol> GetList(MinecraftSocket socket);
        protected abstract Task Update(MinecraftSocket socket, TCol newCol);
        protected abstract string Format(TElem elem);
        /// <summary>
        /// The Long format is used in hover text to help with identification.
        /// By default its the same as <see cref="Format(TElem)"/>
        /// </summary>
        /// <param name="elem"></param>
        /// <returns></returns>
        protected virtual string LongFormat(TElem elem)
        {
            return Format(elem);
        }
        protected abstract string GetId(TElem elem);

        protected class CreationOption
        {
            public TElem Element;
        }
    }
}