using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class BlacklistCommand : ListCommand<ListEntry, List<ListEntry>>
    {
        FilterParser parser = new FilterParser();
        protected override string Format(ListEntry elem)
        {
            return FormatEntry(elem);
        }

        public static string FormatEntry(ListEntry elem)
        {
            return $"{elem.DisplayName ?? elem.ItemTag} {(elem.filter == null ? "" : string.Join(" & ", elem.filter.Select(f => $"{McColorCodes.AQUA}{f.Key}{DEFAULT_COLOR}={McColorCodes.GREEN}{f.Value}")))}";
        }

        protected override string LongFormat(ListEntry elem)
        {
            var formattedTags = elem.Tags == null ? "" : " Tags: " + string.Join(',', elem.Tags.Select(t => $"{McColorCodes.AQUA}{t}{DEFAULT_COLOR}"));
            return Format(elem) + $"\nTag: {elem.ItemTag ?? McColorCodes.BOLD + "all flips are affected by this"}" + formattedTags;
        }

        protected override string GetId(ListEntry elem)
        {
            return $"{elem.ItemTag} {(elem.filter == null ? "" : string.Join(',', elem.filter.Select(f => $"{f.Key}={f.Value}")))}";
        }

        protected override Task<List<ListEntry>> GetList(MinecraftSocket socket)
        {
            SelfUpdatingValue<FlipSettings> settings = GetSettings(socket);
            return Task.FromResult(settings.Value.BlackList);
        }

        protected SelfUpdatingValue<FlipSettings> GetSettings(MinecraftSocket socket)
        {
            var settings = socket.sessionLifesycle.FlipSettings;
            if (settings.Value == null)
                throw new Coflnet.Sky.Core.CoflnetException("login", "Login is required to use this command");
            if (settings.Value.BlackList == null)
                settings.Value.BlackList = new System.Collections.Generic.List<ListEntry>();
            if (settings.Value.WhiteList == null)
                settings.Value.WhiteList = new System.Collections.Generic.List<ListEntry>();
            return settings;
        }

        protected override async Task Update(MinecraftSocket socket, List<ListEntry> newCol)
        {
            var list = GetSettings(socket);
            list.Value.BlackList = newCol;
            await list.Update(list.Value);
        }

        protected override async Task<IEnumerable<CreationOption>> CreateFrom(MinecraftSocket socket, string val)
        {
            var filters = new Dictionary<string, string>();
            var allFilters = FlipFilter.AllFilters.Append("removeAfter");
            if (val.Contains('='))
            {
                val = await parser.ParseFiltersAsync(socket, val, filters, allFilters);
            }
            List<Items.Client.Model.SearchResult> result = new List<Items.Client.Model.SearchResult>();
            var removeAfter = filters.ContainsKey("removeAfter") ? filters["removeAfter"] : null;
            if (removeAfter != null)
            {
                filters.Remove("removeAfter");
            }
            if (val.Length < 1)
            {
                // filter only element
                result.Add(new Items.Client.Model.SearchResult());
            }
            else
                result = await socket.GetService<Items.Client.Api.IItemsApi>().ItemsSearchTermGetAsync(val);
            var isTag = val.ToUpper() == val && !val.Contains(' ');

            if (result == null)
                throw new CoflnetException("search", "Sorry there was no result for your search. If you are sure there should be one please report this");

            return result.Where(r => r?.Flags == null || r.Flags.Value.HasFlag(Items.Client.Model.ItemFlags.AUCTION)).Select(r =>
            {
                var entry = new ListEntry() { ItemTag = r.Tag, DisplayName = r.Text, filter = filters };
                if (removeAfter != null)
                {
                    entry.Tags = new List<string>() { "removeAfter=" + DateTime.Parse(removeAfter).RoundDown(TimeSpan.FromHours(1)).ToString("s") };
                }
                entry.GetExpression().Compile().Invoke(new FlipInstance()
                {
                    Auction = new Core.SaveAuction()
                    {
                        ItemName = "test",
                        Tag = r.Tag,
                        NBTLookup = new List<Core.NBTLookup>(),
                        FlatenedNBT = new Dictionary<string, string>()
                    },
                });

                return new CreationOption()
                {
                    Element = entry
                };
            }).Where(e => !isTag || string.IsNullOrEmpty(val) || e.Element.ItemTag == val);
        }

        /// <inheritdoc/>
        protected override Task<ListEntry> UpdateElem(MinecraftSocket socket, ListEntry current, string args)
        {
            return Task.FromResult(current);
        }
    }
}