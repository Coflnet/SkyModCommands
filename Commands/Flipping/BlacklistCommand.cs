using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC
{
    [CommandDescription("Manage your blacklist",
        "to add use /cl bl add <item> [filterName=Value]",
        "Example /cl bl add Hyperion sharpness=1-5")]
    public class BlacklistCommand : ListCommand<ListEntry, List<ListEntry>>
    {
        public override bool IsPublic => true;
        private FilterParser parser = new FilterParser();
        protected override string Format(ListEntry elem)
        {
            return FormatEntry(elem);
        }

        public static string FormatEntry(ListEntry elem)
        {
            return $"{elem.DisplayName ?? elem.ItemTag} {(elem.filter == null ? "" : string.Join(" & ", elem.filter.Select(f => $"{McColorCodes.AQUA}{f.Key}{DEFAULT_COLOR}={McColorCodes.GREEN}{f.Value}")))}";
        }

        protected override DialogBuilder FormatForList(DialogBuilder d, ListEntry e)
        {
            var formatted = Format(e);
            var actionName = e.Disabled ? "enable" : "disable";
            var prettyActionName = e.Disabled ? $"{McColorCodes.GRAY}(disabled) {McColorCodes.GREEN}[Enable]" : $"{McColorCodes.GRAY}[Disable]";
            return d.Msg(formatted).Msg($" {prettyActionName}", $"/cofl {Slug} e {GetId(e)}|{actionName}", $"Click to {actionName} the filter");
        }

        protected override string LongFormat(ListEntry elem)
        {
            var formattedTags = elem.Tags == null ? "" : " Tags: " + string.Join(',', elem.Tags.Select(t => $"{McColorCodes.AQUA}{t}{DEFAULT_COLOR}"));
            return Format(elem) + $"\nTag: {elem.ItemTag ?? McColorCodes.BOLD + "all flips are affected by this"}" + formattedTags;
        }

        protected override string GetId(ListEntry elem)
        {
            return FormatId(elem);
        }

        public static string FormatId(ListEntry elem)
        {
            return $"{elem.ItemTag}{(elem.filter == null ? "" : string.Join(',', elem.filter.Select(f => $"{f.Key}={f.Value}")))}{string.Join(',', elem.Tags ?? new List<string>())}{elem.Order}{(elem.Disabled ? "disabled" : "")}";
        }

        protected override Task<List<ListEntry>> GetList(MinecraftSocket socket)
        {
            SelfUpdatingValue<FlipSettings> settings = GetSettings(socket);
            var blacklist = settings.Value.BlackList;
            return Task.FromResult(blacklist);
        }

        protected SelfUpdatingValue<FlipSettings> GetSettings(MinecraftSocket socket)
        {
            var settings = socket.sessionLifesycle.FlipSettings;
            if (settings.Value == null)
                throw new CoflnetException("login", "Login is required to use this command");
            if (settings.Value.BlackList == null)
                settings.Value.BlackList = new List<ListEntry>();
            if (settings.Value.WhiteList == null)
                settings.Value.WhiteList = new List<ListEntry>();
            return settings;
        }

        protected override async Task Update(MinecraftSocket socket, List<ListEntry> newCol)
        {
            var list = GetSettings(socket);
            list.Value.BlackList = newCol;
            list.Value.RecompileMatchers();
            list.Value.LastChanged = "preventUpdateMsg";
            list.Value.Changer = socket.SessionInfo.ConnectionId;
            await list.Update(list.Value);
        }

        protected override async Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
        {
            var filters = new Dictionary<string, string>();
            var allFilters = FlipFilter.AllFilters.Append("removeAfter").Append("duration").Append("tag");
            var originalVal = val;
            if (val.Contains('='))
            {
                val = await parser.ParseFiltersAsync(socket, val, filters, allFilters);
            }
            List<SearchResult> result = new List<SearchResult>();
            var removeAfter = filters.ContainsKey("removeAfter") ? filters["removeAfter"] : null;
            if (removeAfter != null)
            {
                filters.Remove("removeAfter");
            }
            if (filters.TryGetValue("tag", out var tag))
            {
                filters.Remove("tag");
            }
            if (filters.TryGetValue("tags", out tag))
            {
                filters.Remove("tags");
            }
            if (filters.ContainsKey("duration"))
            {
                string[] formats = { @"d\d", @"h\h" };
                if (!TimeSpan.TryParseExact(filters["duration"], formats, null, out var span))
                    throw new CoflnetException("invalid_duration", $"The duration `{filters["duration"]}` is not valid, only 12h and 5d are supported");
                removeAfter = DateTime.Now.Add(span).ToString("s");
                filters.Remove("duration");
            }
            if (val.Length < 1)
            {
                // filter only element
                result.Add(new SearchResult() { Text = originalVal });
                Console.WriteLine("filter only element");
            }
            else
                result = await socket.GetService<Items.Client.Api.IItemsApi>().ItemsSearchTermGetAsync(val);
            var isTag = val.ToUpper() == val && !val.Contains(' ');

            if (result == null)
                throw new CoflnetException("search", "Sorry there was no result for your search. If you are sure there should be one please report this");

            return result.Where(r => r?.Flags == null || r.Flags.Value.HasFlag(ItemFlags.AUCTION)).Select(r =>
            {
                var entry = new ListEntry() { ItemTag = r.Tag, DisplayName = r.Text, filter = filters, Tags = tag?.Split(',').ToList() };
                if (removeAfter != null)
                {
                    entry.Tags = new List<string>() { "removeAfter=" + DateTime.Parse(removeAfter).RoundDown(TimeSpan.FromHours(1)).ToString("s") };
                }
                TestForExceptions(socket, r, entry);

                return new CreationOption()
                {
                    Element = entry
                };
            }).Where(e => !isTag || string.IsNullOrEmpty(val) || e.Element.ItemTag == val);
        }

        protected override async Task Edit(MinecraftSocket socket, string subArgs)
        {
            var args = subArgs.Split('|');
            var id = args[0];
            var action = args[1];
            var elements = await GetList(socket);
            var element = elements.FirstOrDefault(e => GetId(e) == id);
            if (element == null)
            {
                socket.Dialog(db => db.Msg("The filter could not be found"));
                return;
            }
            if (action == "enable")
            {
                element.Disabled = false;
                socket.Dialog(db => db.Msg("Enabled the filter"));
            }
            else if (action == "disable")
            {
                element.Disabled = true;
                socket.Dialog(db => db.Msg("Disabled the filter"));
            }
            else if (action == "remove")
            {
                elements.Remove(element);
            }
            await Update(socket, elements);
        }

        private static void TestForExceptions(MinecraftSocket socket, SearchResult r, ListEntry entry)
        {
            try
            {
                entry.GetExpression(socket.SessionInfo).Compile().Invoke(GetTestFlip(r.Tag));
            }
            catch (Exception e)
            {
                Console.WriteLine(JSON.Stringify(entry) + e);
                socket.SendMessage("The filter could not be parsed or created, please check your syntax or report this");
                throw;
            }
        }

        public static FlipInstance GetTestFlip(string tag)
        {
            return new FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    ItemName = "test",
                    Tag = tag ?? "test",
                    Bin = true,
                    StartingBid = 2,
                    NBTLookup = Array.Empty<NBTLookup>(),
                    FlatenedNBT = new(),
                    Enchantments = new(),
                    Context = new()
                },
                Finder = LowPricedAuction.FinderType.SNIPER,
                MedianPrice = 100000000,
                LowestBin = 100000,
                Context = new()
            };
        }

        /// <inheritdoc/>
        protected override Task<ListEntry> UpdateElem(MinecraftSocket socket, ListEntry current, string args)
        {
            return Task.FromResult(current);
        }
    }
}