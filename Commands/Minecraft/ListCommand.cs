using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    public abstract class ListCommand<TElem, TCol> : McCommand where TCol : ICollection<TElem>
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(arguments);
            var subArgs = args.Substring(args.IndexOf(' ') + 1);
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
                    await Add(socket, subArgs);
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

        private async Task Edit(MinecraftSocket socket, string subArgs)
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
                .ForEach(options, (d, o) => d.MsgLine($"{Format(o.Element)} {McColorCodes.YELLOW}[ADD]", $"/cofl {Slug} add !json{JsonConvert.SerializeObject(o.Element)}", $"Add {LongFormat(o.Element)}")));
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

        protected virtual async Task Help(MinecraftSocket socket, string subArgs)
        {
            socket.SendMessage(new DialogBuilder()
                .MsgLine($"usage of {McColorCodes.AQUA}/cofl {Slug}{DEFAULT_COLOR}")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} add{DEFAULT_COLOR} adds a new entry")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} rm{DEFAULT_COLOR} removes an entry")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} list{DEFAULT_COLOR} lists all entries")
                .MsgLine($"{McColorCodes.AQUA}/cofl {Slug} help{DEFAULT_COLOR} display this help"));
        }

        protected virtual async Task Remove(MinecraftSocket socket, string arguments)
        {
            var toRemove = await Find(socket, arguments);
            if (toRemove.Count == 0)
            {
                socket.SendMessage(new DialogBuilder().MsgLine($"Could not find {McColorCodes.AQUA}{arguments}{DEFAULT_COLOR} in list"));
                return;
            }
            var list = await GetList(socket);
            if (toRemove.Count == 1)
            {
                var elem = toRemove.First();
                list.Remove(elem);
                await Update(socket, list);
                socket.SendMessage(new DialogBuilder().MsgLine($"Removed {Format(elem)}"));
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
            int.TryParse(subArgs, out int page);
            var totalPages = list.Count / pageSize;
            if (totalPages < page)
            {
                socket.SendMessage(new DialogBuilder()
                .MsgLine($"There are only {McColorCodes.YELLOW}{totalPages}{McColorCodes.WHITE} pages in total (starting from 0)", null, $"Try running it without or a smaller number"));
                return;
            }

            socket.SendMessage(new DialogBuilder()
                .MsgLine($"Content (page {page}):", $"/cofl {Slug} ls {page + 1}", $"This is page {page} \nthere are {totalPages} pages\nclick this to show the next page")
                .ForEach(list.Skip(page * pageSize).Take(pageSize), (d, e) =>
                {
                    FormatForList(d,e).MsgLine($" {McColorCodes.YELLOW}[REMOVE]{DEFAULT_COLOR}", $"/cofl {Slug} rm {GetId(e)}", $"remove {LongFormat(e)}");
                }));
        }

        protected virtual DialogBuilder FormatForList(DialogBuilder d, TElem e)
        {
            var formatted = Format(e);
            return d.Msg(formatted);
        }

        protected virtual async Task<ICollection<TElem>> Find(MinecraftSocket socket, string val)
        {
            var list = await GetList(socket);
            return list.Where(l => GetId(l).Contains(val)).ToList();
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